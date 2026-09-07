using Microsoft.EntityFrameworkCore;
using Talkaton.Api.Common;
using Talkaton.Domain.Entities;
using Talkaton.Domain.Scheduling;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.Availability;

/// <summary>
/// Грид занятости участников (Этап 7.1) — по мотивам Google Find a Time / Outlook Scheduling
/// Assistant. Отдаёт только занятые интервалы, без темы и деталей встречи: коллега должен
/// видеть «занят», а не содержимое чужого календаря.
/// </summary>
public static class AvailabilityEndpoints
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);

    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/availability", GetAsync)
            .AddEndpointFilter<RequireUserFilter>()
            .WithTags("Availability")
            .WithName("GetAvailability")
            .WithSummary("Busy-интервалы участников за период — грид занятости в диалоге встречи")
            .Produces<IReadOnlyList<UserAvailabilityDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetAsync(
        string? userIds,
        DateTime? from,
        DateTime? to,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        // Любой вошедший может спросить занятость коллеги — это внутренний инструмент
        // планирования, детали встречи всё равно не отдаём, только «занято».
        _ = currentUser.Required;

        var windowStart = AsUtc(from ?? DateTime.UtcNow.Date);
        var windowEnd = AsUtc(to ?? windowStart.AddDays(1));

        if (windowEnd <= windowStart)
        {
            return Results.Problem(
                title: "Пустой период",
                detail: "Параметр to должен быть строго больше from.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (windowEnd - windowStart > MaxWindow)
        {
            return Results.Problem(
                title: "Слишком широкий период",
                detail: $"Не больше {MaxWindow.TotalDays:F0} дней за один запрос.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var ids = ParseIds(userIds);
        if (ids.Count == 0)
        {
            return Results.Ok(Array.Empty<UserAvailabilityDto>());
        }

        // Тентативный статус тоже держит слот занятым — человек ещё может согласиться.
        // Отклонённые встречи заведомо не в счёт.
        var events = await db.Events
            .Include(x => x.Participants)
            .Include(x => x.Overrides)
            .Where(x => x.Participants.Any(p => ids.Contains(p.UserId) && p.Status != ParticipantStatus.Declined))
            .Where(x => x.RecurrenceRule != null
                        || x.Overrides.Count > 0
                        || (x.EndUtc > windowStart && x.StartUtc < windowEnd))
            .AsSplitQuery()
            .ToListAsync(ct);

        // Буфер (Этап 7.5) — своя настройка каждого человека, растягивает занятый интервал
        // в обе стороны, чтобы соседнюю встречу не ставили впритык.
        var buffers = await db.Users
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.BufferBeforeMinutes, x.BufferAfterMinutes })
            .ToDictionaryAsync(x => x.Id, ct);

        var result = new List<UserAvailabilityDto>(ids.Count);
        foreach (var userId in ids)
        {
            var mine = events.Where(x => x.Participants.Any(p => p.UserId == userId && p.Status != ParticipantStatus.Declined));
            var occurrences = OccurrenceCalculator.Expand(mine, windowStart, windowEnd);

            var before = buffers.TryGetValue(userId, out var buffer) ? buffer.BufferBeforeMinutes : 0;
            var after = buffers.TryGetValue(userId, out buffer) ? buffer.BufferAfterMinutes : 0;

            var padded = occurrences.Select(x => (
                Start: x.StartUtc.AddMinutes(-before),
                End: x.EndUtc.AddMinutes(after)));

            var busy = MergeIntervals(padded)
                .Select(x => new BusyBlockDto(x.Start, x.End))
                .ToList();

            result.Add(new UserAvailabilityDto(userId, busy));
        }

        return Results.Ok(result);
    }

    private static HashSet<Guid> ParseIds(string? raw)
    {
        var ids = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ids;
        }

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Разные встречи одного человека часто пересекаются или идут впритык — сливаем
    /// в непрерывные полосы занятости, иначе грид рисовал бы наложенные друг на друга блоки.
    /// </summary>
    private static List<(DateTime Start, DateTime End)> MergeIntervals(IEnumerable<(DateTime Start, DateTime End)> intervals)
    {
        var sorted = intervals.OrderBy(x => x.Start).ToList();
        var merged = new List<(DateTime Start, DateTime End)>();

        foreach (var interval in sorted)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                if (interval.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, interval.End);
                }
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
