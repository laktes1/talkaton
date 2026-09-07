using Microsoft.EntityFrameworkCore;
using Talkaton.Api.Common;
using Talkaton.Domain.Entities;
using Talkaton.Domain.Recurrence;
using Talkaton.Domain.Scheduling;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.Events;

public static class EventEndpoints
{
    /// <summary>
    /// Потолок запрашиваемого периода. Вид «Год» просит 366 дней; всё, что сильно больше,
    /// — либо опечатка в параметрах, либо попытка развернуть ежедневную серию на век вперёд.
    /// </summary>
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(800);

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events")
            .AddEndpointFilter<RequireUserFilter>()
            .WithTags("Events");

        group.MapGet("/", GetOccurrencesAsync)
            .WithName("GetEvents")
            .WithSummary("Вхождения встреч за период — то, что рисуется в сетке")
            .Produces<IReadOnlyList<OccurrenceDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetEvent")
            .WithSummary("Правая панель встречи: участники и артефакты")
            .Produces<EventDetailsDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/artifacts", GetArtifactsAsync)
            .WithName("GetEventArtifacts")
            .WithSummary("Блок «Артефакты встречи»")
            .Produces<IReadOnlyList<ArtifactDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateEvent")
            .WithSummary("Создание встречи из диалога «Создать встречу»")
            .Produces<EventDetailsDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateEvent")
            .WithSummary("Правка встречи, перенос drag&drop и resize границ")
            .Produces<EventDetailsDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteEvent")
            .WithSummary("Удаление серии или одного вхождения")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/rsvp", RsvpAsync)
            .WithName("RsvpEvent")
            .WithSummary("Ответ участника: идёт / не идёт / возможно")
            .Produces<EventDetailsDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetOccurrencesAsync(
        DateTime? from,
        DateTime? to,
        string? calendarIds,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var viewer = currentUser.Required;
        var windowStart = AsUtc(from ?? DateTime.UtcNow.Date);
        var windowEnd = AsUtc(to ?? windowStart.AddDays(7));

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

        var wanted = ParseCalendarFilter(calendarIds);

        // Разовые встречи отсекаем по времени в SQL. Серии тянем целиком: их немного,
        // а понять, попадает ли RRULE в окно, всё равно можно только развернув правило.
        var loaded = await Visible(db, viewer.Id)
            .Where(x => x.RecurrenceRule != null
                        || x.Overrides.Count > 0
                        || (x.EndUtc > windowStart && x.StartUtc < windowEnd))
            .ToListAsync(ct);

        var occurrences = OccurrenceCalculator.Expand(loaded, windowStart, windowEnd)
            .Where(x => IsInFilter(x.Event, viewer.Id, wanted))
            .Select(x => EventMapper.ToDto(x, viewer.Id))
            .ToList();

        return Results.Ok(occurrences);
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        DateTime? occurrenceStart,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var viewer = currentUser.Required;
        var source = await Visible(db, viewer.Id).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return Results.NotFound();
        }

        var occurrence = ResolveOccurrence(source, occurrenceStart);
        return occurrence is null
            ? Results.NotFound()
            : Results.Ok(EventMapper.ToDetails(occurrence.Value, viewer.Id));
    }

    private static async Task<IResult> GetArtifactsAsync(
        Guid id,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var viewer = currentUser.Required;
        var source = await Visible(db, viewer.Id).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return Results.NotFound();
        }

        var artifacts = source.Artifacts
            .OrderBy(x => x.SortOrder)
            .Select(EventMapper.ToDto)
            .ToList();

        return Results.Ok(artifacts);
    }

    private static async Task<IResult> CreateAsync(
        CreateEventRequest request,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var actingUser = currentUser.Required;

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return Problem("Пустая тема встречи", "Тема обязательна — по ней встречу узнают в сетке.");
        }

        var startUtc = AsUtc(request.StartUtc);
        var endUtc = AsUtc(request.EndUtc);
        if (endUtc <= startUtc)
        {
            return Problem("Встреча заканчивается раньше, чем начинается", "Конец должен быть позже начала.");
        }

        // Делегирование (Этап 7.6): «от чьего имени» встреча — сам актор по умолчанию,
        // либо тот, кто явно делегировал ему право управлять своим календарём.
        var organizerId = actingUser.Id;
        if (request.OnBehalfOfUserId is { } onBehalfOf && onBehalfOf != actingUser.Id)
        {
            if (!await db.Delegations.AnyAsync(x => x.OwnerId == onBehalfOf && x.DelegateId == actingUser.Id, ct))
            {
                return Results.Problem(
                    title: "Нет права создавать встречи от имени этого человека",
                    detail: "Попросите его сначала выдать вам делегирование на управление календарём.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            organizerId = onBehalfOf;
        }

        var calendar = await db.Calendars
            .FirstOrDefaultAsync(x => x.Id == request.CalendarId && x.OwnerId == organizerId, ct);
        if (calendar is null)
        {
            return Problem("Неизвестный календарь", "Создавать встречи можно только в календарях того, от чьего имени вы действуете.");
        }

        if (await ValidateRoomAsync(db, request.RoomId, startUtc, endUtc, excludeEventId: null, ct) is { } roomError)
        {
            return roomError;
        }

        string? rrule = null;
        if (!string.IsNullOrWhiteSpace(request.RecurrenceRule))
        {
            if (!RecurrenceRule.TryParse(request.RecurrenceRule, out var parsed, out var error) || parsed is null)
            {
                return Problem("Не разобрано правило повторяемости", error);
            }

            rrule = parsed.ToRRule();
        }

        var now = DateTime.UtcNow;
        var meeting = new Event
        {
            Id = Guid.NewGuid(),
            CalendarId = calendar.Id,
            OrganizerId = organizerId,
            CreatedByUserId = actingUser.Id,
            Title = title,
            Description = request.Description?.Trim(),
            StartUtc = startUtc,
            EndUtc = endUtc,
            IsAllDay = request.IsAllDay,
            RecurrenceRule = rrule,
            TalkRoomSlug = string.IsNullOrWhiteSpace(request.TalkRoomSlug) ? null : request.TalkRoomSlug.Trim(),
            RoomId = request.RoomId,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        meeting.Participants.Add(new EventParticipant
        {
            EventId = meeting.Id,
            UserId = organizerId,
            Status = ParticipantStatus.Accepted,
            IsOrganizer = true,
        });

        await AddParticipantsAsync(db, meeting, request.ParticipantIds, organizerId, ct);

        var reminder = NormalizeReminder(request.ReminderMinutesBefore);
        foreach (var participant in meeting.Participants)
        {
            meeting.Reminders.Add(new Reminder
            {
                EventId = meeting.Id,
                UserId = participant.UserId,
                MinutesBefore = reminder,
            });
        }

        if (request.GenerateArtifacts == true)
        {
            SeedArtifacts(meeting);
        }

        db.Events.Add(meeting);
        await db.SaveChangesAsync(ct);

        var saved = await Visible(db, organizerId).FirstAsync(x => x.Id == meeting.Id, ct);
        var occurrence = ResolveOccurrence(saved, occurrenceStart: null);
        return occurrence is null
            ? Results.NoContent()
            : Results.Created($"/api/events/{meeting.Id}", EventMapper.ToDetails(occurrence.Value, organizerId));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateEventRequest request,
        string? scope,
        DateTime? occurrenceStart,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var viewer = currentUser.Required;
        var source = await Visible(db, viewer.Id).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return Results.NotFound();
        }

        var reminderOnly = request is
        {
            CalendarId: null, Title: null, Description: null, StartUtc: null, EndUtc: null,
            IsAllDay: null, RecurrenceRule: null, ClearRecurrence: null, TalkRoomSlug: null, ParticipantIds: null,
            GenerateArtifacts: null, RoomId: null, ClearRoom: null,
        };

        // Участник встречи может настроить себе напоминание, но не переписать чужую встречу.
        // Правит организатор либо его делегат (Этап 7.6) — секретарь тоже может редактировать.
        if (!await CanManageAsync(db, viewer.Id, source.OrganizerId, ct) && !reminderOnly)
        {
            return Results.Problem(
                title: "Встречу правит организатор",
                detail: "Вам доступны только ответ на приглашение и своё напоминание.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.ReminderMinutesBefore is { } minutes)
        {
            UpsertReminder(db, source, viewer.Id, NormalizeReminder(minutes));
        }

        if (request.GenerateArtifacts == true)
        {
            SeedArtifacts(source, db);
        }

        var effectiveScope = ParseScope(scope);
        var result = effectiveScope == EditScope.Occurrence
            ? ApplyToOccurrence(request, source, occurrenceStart, db)
            : await ApplyToSeriesAsync(request, source, source.OrganizerId, db, ct);

        if (result is not null)
        {
            return result;
        }

        source.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var reloaded = await Visible(db, viewer.Id).FirstAsync(x => x.Id == id, ct);
        var occurrence = ResolveOccurrence(reloaded, effectiveScope == EditScope.Occurrence ? occurrenceStart : null);
        return occurrence is null
            ? Results.NoContent()
            : Results.Ok(EventMapper.ToDetails(occurrence.Value, viewer.Id));
    }

    /// <summary>Перенос одного вхождения серии — исключение вместо правки всей серии.</summary>
    private static IResult? ApplyToOccurrence(
        UpdateEventRequest request,
        Event source,
        DateTime? occurrenceStart,
        TalkatonDbContext db)
    {
        if (occurrenceStart is null)
        {
            return Problem("Не указано вхождение", "Для scope=occurrence нужен параметр occurrenceStart.");
        }

        var onlyTimeChanged = request is
        {
            CalendarId: null, Title: null, Description: null, IsAllDay: null,
            RecurrenceRule: null, ClearRecurrence: null, TalkRoomSlug: null, ParticipantIds: null,
            GenerateArtifacts: null, RoomId: null, ClearRoom: null,
        };

        if (!onlyTimeChanged)
        {
            return Problem(
                "Слишком много для одного вхождения",
                "У вхождения своё только время. Остальное правится с scope=series.");
        }

        var key = AsUtc(occurrenceStart.Value);
        var current = OccurrenceCalculator.Find(source, key);
        if (current is null)
        {
            return Results.NotFound();
        }

        var start = request.StartUtc is { } newStart ? AsUtc(newStart) : current.Value.StartUtc;
        var end = request.EndUtc is { } newEnd ? AsUtc(newEnd) : current.Value.EndUtc;
        if (end <= start)
        {
            return Problem("Встреча заканчивается раньше, чем начинается", "Конец должен быть позже начала.");
        }

        var patch = EnsureOverride(db, source, key);
        patch.StartUtc = start;
        patch.EndUtc = end;
        patch.IsCancelled = false;

        return null;
    }

    /// <summary>
    /// Находит исключение вхождения или заводит новое. Новое кладём через DbSet, а не в
    /// навигацию: Id уже проставлен, и в графе изменений EF принял бы такую строку за правку
    /// существующей. В <c>source.Overrides</c> её тут же положит фиксап связей — вручную
    /// добавлять второй раз нельзя, список примет тот же объект дважды.
    /// </summary>
    private static EventOccurrenceOverride EnsureOverride(TalkatonDbContext db, Event source, DateTime key)
    {
        var patch = source.Overrides.FirstOrDefault(x => x.OriginalStartUtc == key);
        if (patch is not null)
        {
            return patch;
        }

        patch = new EventOccurrenceOverride { Id = Guid.NewGuid(), EventId = source.Id, OriginalStartUtc = key };
        db.EventOccurrenceOverrides.Add(patch);
        return patch;
    }

    /// <summary>Правка серии. Возвращает <c>null</c>, если всё в порядке, иначе — ответ с ошибкой.</summary>
    private static async Task<IResult?> ApplyToSeriesAsync(
        UpdateEventRequest request,
        Event source,
        Guid organizerId,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        if (request.CalendarId is { } calendarId && calendarId != source.CalendarId)
        {
            var target = await db.Calendars.FirstOrDefaultAsync(x => x.Id == calendarId && x.OwnerId == organizerId, ct);
            if (target is null)
            {
                return Problem("Неизвестный календарь", "Переносить встречу можно только в свой календарь.");
            }

            source.CalendarId = target.Id;
        }

        if (request.Title is { } title)
        {
            var trimmed = title.Trim();
            if (trimmed.Length == 0)
            {
                return Problem("Пустая тема встречи", "Тема обязательна.");
            }

            source.Title = trimmed;
        }

        if (request.Description is { } description)
        {
            source.Description = description.Trim() is { Length: > 0 } text ? text : null;
        }

        if (request.TalkRoomSlug is { } slug)
        {
            source.TalkRoomSlug = slug.Trim() is { Length: > 0 } text ? text : null;
        }

        if (request.IsAllDay is { } isAllDay)
        {
            source.IsAllDay = isAllDay;
        }

        var start = request.StartUtc is { } newStart ? AsUtc(newStart) : source.StartUtc;
        var end = request.EndUtc is { } newEnd ? AsUtc(newEnd) : source.EndUtc;
        if (end <= start)
        {
            return Problem("Встреча заканчивается раньше, чем начинается", "Конец должен быть позже начала.");
        }

        if (request.ClearRoom == true)
        {
            source.RoomId = null;
        }
        else if (request.RoomId is { } roomId)
        {
            if (await ValidateRoomAsync(db, roomId, start, end, source.Id, ct) is { } roomError)
            {
                return roomError;
            }

            source.RoomId = roomId;
        }
        else if (source.RoomId is { } existingRoomId && (start != source.StartUtc || end != source.EndUtc))
        {
            // Время серии сдвинулось, а переговорка осталась прежней — проверяем, что она
            // всё ещё свободна на новое время, а не только на старое.
            if (await ValidateRoomAsync(db, existingRoomId, start, end, source.Id, ct) is { } roomError)
            {
                return roomError;
            }
        }

        if (start != source.StartUtc)
        {
            // Сдвиг всей серии сбивает привязку исключений к расписанию: их ключи
            // указывают на старый ритм. Честнее начать серию заново, чем показывать призраков.
            source.Overrides.Clear();
        }

        source.StartUtc = start;
        source.EndUtc = end;

        if (request.ClearRecurrence == true)
        {
            source.RecurrenceRule = null;
            source.Overrides.Clear();
        }
        else if (!string.IsNullOrWhiteSpace(request.RecurrenceRule))
        {
            if (!RecurrenceRule.TryParse(request.RecurrenceRule, out var parsed, out var error) || parsed is null)
            {
                return Problem("Не разобрано правило повторяемости", error);
            }

            var canonical = parsed.ToRRule();
            if (canonical != source.RecurrenceRule)
            {
                source.RecurrenceRule = canonical;
                source.Overrides.Clear();
            }
        }

        if (request.ParticipantIds is not null)
        {
            await ReplaceParticipantsAsync(db, source, request.ParticipantIds, ct);
        }

        return null;
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        string? scope,
        DateTime? occurrenceStart,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var viewer = currentUser.Required;
        var source = await Visible(db, viewer.Id).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return Results.NotFound();
        }

        if (!await CanManageAsync(db, viewer.Id, source.OrganizerId, ct))
        {
            return Results.Problem(
                title: "Встречу удаляет организатор",
                detail: "Если вы не идёте — ответьте «не идёт», встреча останется у остальных.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (ParseScope(scope) == EditScope.Occurrence)
        {
            if (occurrenceStart is null)
            {
                return Problem("Не указано вхождение", "Для scope=occurrence нужен параметр occurrenceStart.");
            }

            var key = AsUtc(occurrenceStart.Value);
            if (OccurrenceCalculator.Find(source, key) is null)
            {
                return Results.NotFound();
            }

            EnsureOverride(db, source, key).IsCancelled = true;
            source.UpdatedUtc = DateTime.UtcNow;
        }
        else
        {
            db.Events.Remove(source);
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RsvpAsync(
        Guid id,
        RsvpRequest request,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var viewer = currentUser.Required;

        if (!ParticipantStatusCodes.TryParse(request.Status, out var status))
        {
            return Problem(
                "Неизвестный ответ",
                "Допустимо только accepted, declined или tentative.");
        }

        var source = await Visible(db, viewer.Id).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return Results.NotFound();
        }

        var participant = source.Participants.FirstOrDefault(x => x.UserId == viewer.Id);
        if (participant is null)
        {
            return Problem("Вы не участник этой встречи", "Ответить можно только на своё приглашение.");
        }

        participant.Status = status;
        await db.SaveChangesAsync(ct);

        var reloaded = await Visible(db, viewer.Id).FirstAsync(x => x.Id == id, ct);
        var occurrence = ResolveOccurrence(reloaded, occurrenceStart: null);
        return occurrence is null
            ? Results.NoContent()
            : Results.Ok(EventMapper.ToDetails(occurrence.Value, viewer.Id));
    }

    /// <summary>
    /// Встречи, которые человек имеет право видеть: свои календари плюс всё,
    /// куда его позвали. Единственное место, где задаётся эта граница.
    /// </summary>
    /// <summary>
    /// Тумблер «Запись + ИИ-протокол»: заводит запись и протокол встречи. Повторный вызов
    /// ничего не делает — артефакты уже есть, а снятие тумблера их не удаляет: запись
    /// прошедшей встречи не должна пропадать от правки карточки.
    /// </summary>
    /// <param name="db">
    /// Нужен для правки уже сохранённой встречи: Id у артефакта проставлен заранее, и в графе
    /// изменений EF принял бы строку, положенную только в навигацию, за правку существующей —
    /// ровно та же ловушка, что и в <see cref="EnsureOverride"/>. Добавляем через DbSet,
    /// в <c>meeting.Artifacts</c> строку положит фиксап связей. При создании встречи весь граф
    /// и так уходит в Added, поэтому там контекст не нужен.
    /// </param>
    private static void SeedArtifacts(Event meeting, TalkatonDbContext? db = null)
    {
        if (meeting.Artifacts.Count > 0)
        {
            return;
        }

        var seeded = new[]
        {
            new EventArtifact
            {
                Id = Guid.NewGuid(),
                EventId = meeting.Id,
                Kind = ArtifactKind.Recording,
                Title = "Запись встречи",
                Subtitle = "Автозапись Толк",
                SortOrder = 0,
            },
            new EventArtifact
            {
                Id = Guid.NewGuid(),
                EventId = meeting.Id,
                Kind = ArtifactKind.Protocol,
                Title = "Протокол совещания",
                Subtitle = "ИИ-конспект формируется",
                SortOrder = 1,
            },
        };

        if (db is null)
        {
            foreach (var artifact in seeded)
            {
                meeting.Artifacts.Add(artifact);
            }

            return;
        }

        db.EventArtifacts.AddRange(seeded);
    }

    private static IQueryable<Event> Visible(TalkatonDbContext db, Guid viewerId) => db.Events
        .Include(x => x.Calendar)
        .Include(x => x.Organizer)
        .Include(x => x.Participants).ThenInclude(x => x.User)
        .Include(x => x.Artifacts)
        .Include(x => x.Overrides)
        .Include(x => x.Reminders)
        .Include(x => x.Room)
        .Include(x => x.CreatedByUser)
        .AsSplitQuery()
        .Where(x => x.Calendar!.OwnerId == viewerId
                    || x.Participants.Any(p => p.UserId == viewerId)
                    // Делегат (Этап 7.6) видит весь календарь того, кто ему делегировал доступ —
                    // так же, как секретарь видит календарь руководителя целиком, не только свои встречи.
                    || db.Delegations.Any(d => d.OwnerId == x.Calendar!.OwnerId && d.DelegateId == viewerId));

    /// <summary>
    /// Галочки видимости фильтруют только собственные календари. Встречу, куда позвали
    /// снаружи, скрывать нечем — в левой панели такого календаря просто нет.
    /// </summary>
    private static bool IsInFilter(Event source, Guid viewerId, HashSet<Guid>? wanted) =>
        wanted is null
        || wanted.Contains(source.CalendarId)
        || source.Calendar?.OwnerId != viewerId;

    private static HashSet<Guid>? ParseCalendarFilter(string? calendarIds)
    {
        if (string.IsNullOrWhiteSpace(calendarIds))
        {
            return null;
        }

        var ids = new HashSet<Guid>();
        foreach (var raw in calendarIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(raw, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static EventOccurrence? ResolveOccurrence(Event source, DateTime? occurrenceStart)
    {
        if (occurrenceStart is { } key)
        {
            return OccurrenceCalculator.Find(source, AsUtc(key));
        }

        // Без явного ключа показываем ближайшее вхождение от сегодняшнего дня,
        // а если серия целиком в прошлом — самое первое.
        var now = DateTime.UtcNow;
        var ahead = OccurrenceCalculator.Expand([source], now.AddHours(-12), now.AddDays(400));
        if (ahead.Count > 0)
        {
            return ahead[0];
        }

        var behind = OccurrenceCalculator.Expand([source], source.StartUtc.AddSeconds(-1), now);
        return behind.Count > 0 ? behind[0] : null;
    }

    private static async Task AddParticipantsAsync(
        TalkatonDbContext db,
        Event meeting,
        Guid[]? participantIds,
        Guid organizerId,
        CancellationToken ct)
    {
        foreach (var userId in (participantIds ?? []).Distinct())
        {
            if (userId == organizerId || meeting.Participants.Any(x => x.UserId == userId))
            {
                continue;
            }

            if (await db.Users.AnyAsync(x => x.Id == userId, ct))
            {
                meeting.Participants.Add(new EventParticipant
                {
                    EventId = meeting.Id,
                    UserId = userId,
                    Status = ParticipantStatus.Tentative,
                });
            }
        }
    }

    private static async Task ReplaceParticipantsAsync(
        TalkatonDbContext db,
        Event source,
        Guid[] participantIds,
        CancellationToken ct)
    {
        var wanted = participantIds.Distinct().Where(x => x != source.OrganizerId).ToHashSet();

        foreach (var participant in source.Participants.Where(x => !x.IsOrganizer).ToList())
        {
            if (!wanted.Remove(participant.UserId))
            {
                source.Participants.Remove(participant);
                db.Reminders.RemoveRange(source.Reminders.Where(x => x.UserId == participant.UserId));
            }
        }

        foreach (var userId in wanted)
        {
            if (await db.Users.AnyAsync(x => x.Id == userId, ct))
            {
                // Через DbSet: у составного ключа обе половины заданы, и в графе изменений
                // EF принял бы нового участника за правку уже существующей строки.
                // В source.Participants его положит фиксап связей.
                db.EventParticipants.Add(new EventParticipant
                {
                    EventId = source.Id,
                    UserId = userId,
                    Status = ParticipantStatus.Tentative,
                });
            }
        }
    }

    private static void UpsertReminder(TalkatonDbContext db, Event source, Guid userId, int minutes)
    {
        var reminder = source.Reminders.FirstOrDefault(x => x.UserId == userId);
        if (reminder is null)
        {
            db.Reminders.Add(new Reminder { EventId = source.Id, UserId = userId, MinutesBefore = minutes });
            return;
        }

        reminder.MinutesBefore = minutes;
    }

    /// <summary>Напоминание за сутки и больше уже не «перед началом» — обрезаем по здравому смыслу.</summary>
    private static int NormalizeReminder(int? minutes) => Math.Clamp(minutes ?? 10, 0, 24 * 60);

    /// <summary>
    /// System.Text.Json отдаёт время с Kind=Local, если в строке был часовой пояс.
    /// Приводим на границе, чтобы дальше по коду UTC означал UTC.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>Значение по умолчанию — серия: правка без явного scope меняет встречу целиком.</summary>
    private static EditScope ParseScope(string? scope) =>
        string.Equals(scope?.Trim(), "occurrence", StringComparison.OrdinalIgnoreCase)
            ? EditScope.Occurrence
            : EditScope.Series;

    private static IResult Problem(string title, string? detail) =>
        Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Право редактировать/удалять встречу (Этап 7.6): сам организатор либо тот, кому
    /// организатор явно делегировал управление своим календарём (см. <see cref="Delegation"/>).
    /// </summary>
    private static async Task<bool> CanManageAsync(TalkatonDbContext db, Guid viewerId, Guid organizerId, CancellationToken ct) =>
        viewerId == organizerId
        || await db.Delegations.AnyAsync(x => x.OwnerId == organizerId && x.DelegateId == viewerId, ct);

    /// <summary>
    /// Переговорка (Этап 7.2) — общий ресурс: нельзя забронировать то же время в двух встречах
    /// сразу. Смотрим только на прямое пересечение окна [start, end) — этого достаточно,
    /// потому что <see cref="OccurrenceCalculator.Expand"/> уже отдаёт лишь то, что окно задевает.
    /// </summary>
    private static async Task<IResult?> ValidateRoomAsync(
        TalkatonDbContext db,
        Guid? roomId,
        DateTime start,
        DateTime end,
        Guid? excludeEventId,
        CancellationToken ct)
    {
        if (roomId is not { } id)
        {
            return null;
        }

        var exists = await db.Rooms.AnyAsync(x => x.Id == id, ct);
        if (!exists)
        {
            return Problem("Неизвестная переговорка", "Такой комнаты нет в списке переговорок.");
        }

        var candidates = await db.Events
            .Include(x => x.Overrides)
            .Where(x => x.RoomId == id && x.Id != excludeEventId)
            .Where(x => x.RecurrenceRule != null || x.Overrides.Count > 0 || (x.EndUtc > start && x.StartUtc < end))
            .ToListAsync(ct);

        var conflict = OccurrenceCalculator.Expand(candidates, start, end).Count > 0;
        return conflict
            ? Problem("Переговорка занята", "На это время комната уже забронирована другой встречей.")
            : null;
    }
}
