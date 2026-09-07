using Microsoft.EntityFrameworkCore;
using Talkaton.Api.Availability;
using Talkaton.Api.Common;
using Talkaton.Domain.Scheduling;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.Rooms;

/// <summary>Переговорки (Этап 7.2) — список общих комнат и их занятость.</summary>
public static class RoomEndpoints
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);

    public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rooms")
            .AddEndpointFilter<RequireUserFilter>()
            .WithTags("Rooms");

        group.MapGet("/", GetRoomsAsync)
            .WithName("GetRooms")
            .WithSummary("Список переговорок для выбора при создании встречи")
            .Produces<IReadOnlyList<RoomDto>>();

        group.MapGet("/availability", GetAvailabilityAsync)
            .WithName("GetRoomAvailability")
            .WithSummary("Busy-интервалы переговорок за период")
            .Produces<IReadOnlyList<RoomAvailabilityDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetRoomsAsync(TalkatonDbContext db, CancellationToken ct)
    {
        var rooms = await db.Rooms.OrderBy(x => x.Name).ToListAsync(ct);
        return Results.Ok(rooms.Select(RoomDto.From).ToList());
    }

    private static async Task<IResult> GetAvailabilityAsync(
        string? roomIds,
        DateTime? from,
        DateTime? to,
        TalkatonDbContext db,
        CancellationToken ct)
    {
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

        var ids = ParseIds(roomIds);
        if (ids.Count == 0)
        {
            ids = await db.Rooms.Select(x => x.Id).ToListAsync(ct) is { } all ? all.ToHashSet() : [];
        }

        if (ids.Count == 0)
        {
            return Results.Ok(Array.Empty<RoomAvailabilityDto>());
        }

        var events = await db.Events
            .Include(x => x.Overrides)
            .Where(x => x.RoomId != null && ids.Contains(x.RoomId!.Value))
            .Where(x => x.RecurrenceRule != null
                        || x.Overrides.Count > 0
                        || (x.EndUtc > windowStart && x.StartUtc < windowEnd))
            .ToListAsync(ct);

        var result = new List<RoomAvailabilityDto>(ids.Count);
        foreach (var roomId in ids)
        {
            var mine = events.Where(x => x.RoomId == roomId);
            var occurrences = OccurrenceCalculator.Expand(mine, windowStart, windowEnd);
            var busy = MergeIntervals(occurrences.Select(x => (x.StartUtc, x.EndUtc)))
                .Select(x => new BusyBlockDto(x.Start, x.End))
                .ToList();

            result.Add(new RoomAvailabilityDto(roomId, busy));
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

    /// <summary>Та же логика склейки соседних интервалов, что и в грид занятости участников.</summary>
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
