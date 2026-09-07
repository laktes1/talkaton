namespace Talkaton.Domain.Entities;

/// <summary>
/// Встреча. В базе лежит только серия: начало, конец и RRULE. Конкретные вхождения
/// считаются на чтении (см. <c>OccurrenceCalculator</c>), в БД попадают лишь
/// исключения — <see cref="EventOccurrenceOverride"/>.
/// </summary>
public class Event
{
    public Guid Id { get; set; }

    public Guid CalendarId { get; set; }
    public Calendar? Calendar { get; set; }

    public Guid OrganizerId { get; set; }
    public User? Organizer { get; set; }

    /// <summary>
    /// Кто фактически завёл встречу (Этап 7.6) — обычно совпадает с <see cref="OrganizerId"/>,
    /// но при делегировании встречу создаёт помощник от имени руководителя: организатор —
    /// руководитель, а здесь остаётся правда о том, чьими руками это сделано.
    /// Пусто только у встреч, заведённых до появления этой колонки — читать как «совпадает
    /// с организатором».
    /// </summary>
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }

    /// <summary>Начало первого вхождения серии, всегда UTC.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Конец первого вхождения. Длительность одинакова у всех вхождений серии.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Событие полосы «весь день» над сеткой.</summary>
    public bool IsAllDay { get; set; }

    /// <summary>
    /// RRULE по RFC 5545 без префикса «RRULE:», например «FREQ=WEEKLY;BYDAY=WE».
    /// <c>null</c> — разовая встреча.
    /// </summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>Хвост ссылки на комнату Толка: «pdata-hq» из talk.kontur.ru/c/pdata-hq.</summary>
    public string? TalkRoomSlug { get; set; }

    /// <summary>Забронированная переговорка (Этап 7.2) — для очных/гибридных встреч. Необязательна.</summary>
    public Guid? RoomId { get; set; }
    public Room? Room { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ICollection<EventParticipant> Participants { get; set; } = new List<EventParticipant>();
    public ICollection<EventArtifact> Artifacts { get; set; } = new List<EventArtifact>();
    public ICollection<EventOccurrenceOverride> Overrides { get; set; } = new List<EventOccurrenceOverride>();
    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();

    public TimeSpan Duration => EndUtc - StartUtc;
}
