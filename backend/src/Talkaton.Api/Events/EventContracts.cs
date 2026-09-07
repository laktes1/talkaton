using Talkaton.Api.Common;
using Talkaton.Domain.Entities;
using Talkaton.Domain.Scheduling;

namespace Talkaton.Api.Events;

/// <summary>
/// Вхождение встречи — то, что рисует одну карточку в сетке. У разовой встречи оно одно.
/// </summary>
/// <param name="OccurrenceStartUtc">
/// Ключ вхождения внутри серии: с ним фронт возвращается в PATCH и DELETE,
/// когда двигает или удаляет одно вхождение, а не всю серию.
/// </param>
public record OccurrenceDto(
    Guid EventId,
    DateTime OccurrenceStartUtc,
    DateTime StartUtc,
    DateTime EndUtc,
    string Title,
    bool IsAllDay,
    Guid CalendarId,
    string CalendarName,
    string CalendarColor,
    string? RecurrenceRule,
    bool IsMoved,
    string? TalkRoomSlug,
    Guid OrganizerId,
    string OrganizerName,
    bool IsOrganizer,
    string? MyStatus,
    int ParticipantCount,
    int ArtifactCount,
    int? ReminderMinutesBefore,
    Guid? RoomId,
    string? RoomName,
    Guid CreatedByUserId,
    string CreatedByName);

public record ParticipantDto(
    Guid UserId,
    string DisplayName,
    string Status,
    bool IsOrganizer,
    int AvatarColorIndex);

public record ArtifactDto(Guid Id, string Kind, string Title, string? Subtitle, string? Url);

/// <summary>Правая панель встречи целиком: шапка вхождения плюс участники и артефакты.</summary>
public record EventDetailsDto(
    OccurrenceDto Occurrence,
    string? Description,
    IReadOnlyList<ParticipantDto> Participants,
    IReadOnlyList<ArtifactDto> Artifacts);

public record CreateEventRequest(
    Guid CalendarId,
    string Title,
    string? Description,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAllDay,
    string? RecurrenceRule,
    string? TalkRoomSlug,
    Guid[]? ParticipantIds,
    int? ReminderMinutesBefore,
    bool? GenerateArtifacts = false,
    Guid? RoomId = null,
    Guid? OnBehalfOfUserId = null);

/// <summary>
/// Все поля необязательные: drag&amp;drop шлёт только время, редактор — только изменённое.
/// <c>ClearRecurrence</c> нужен отдельным флагом, потому что <c>null</c> в RecurrenceRule
/// означает «не трогать», а не «сделать разовой».
/// </summary>
public record UpdateEventRequest(
    Guid? CalendarId,
    string? Title,
    string? Description,
    DateTime? StartUtc,
    DateTime? EndUtc,
    bool? IsAllDay,
    string? RecurrenceRule,
    bool? ClearRecurrence,
    string? TalkRoomSlug,
    Guid[]? ParticipantIds,
    int? ReminderMinutesBefore,
    bool? GenerateArtifacts = null,
    Guid? RoomId = null,
    bool? ClearRoom = null);

public record RsvpRequest(string Status);

/// <summary>К чему относится правка: ко всей серии или к одному вхождению.</summary>
public enum EditScope
{
    Series,
    Occurrence,
}

public static class EventMapper
{
    public static OccurrenceDto ToDto(EventOccurrence occurrence, Guid viewerId)
    {
        var source = occurrence.Event;
        var mine = source.Participants.FirstOrDefault(x => x.UserId == viewerId);
        var reminder = source.Reminders.FirstOrDefault(x => x.UserId == viewerId);

        return new OccurrenceDto(
            source.Id,
            occurrence.OccurrenceStartUtc,
            occurrence.StartUtc,
            occurrence.EndUtc,
            source.Title,
            source.IsAllDay,
            source.CalendarId,
            source.Calendar?.Name ?? string.Empty,
            source.Calendar?.Color ?? "#4c8dff",
            source.RecurrenceRule,
            occurrence.IsMoved,
            source.TalkRoomSlug,
            source.OrganizerId,
            source.Organizer?.DisplayName ?? string.Empty,
            source.OrganizerId == viewerId,
            mine is null ? null : ParticipantStatusCodes.ToCode(mine.Status),
            source.Participants.Count,
            source.Artifacts.Count,
            reminder?.MinutesBefore,
            source.RoomId,
            source.Room?.Name,
            source.CreatedByUserId ?? source.OrganizerId,
            source.CreatedByUserId is null
                ? source.Organizer?.DisplayName ?? string.Empty
                : source.CreatedByUser?.DisplayName ?? string.Empty);
    }

    public static EventDetailsDto ToDetails(EventOccurrence occurrence, Guid viewerId)
    {
        var source = occurrence.Event;

        var participants = source.Participants
            .Where(x => x.User is not null)
            .OrderByDescending(x => x.IsOrganizer)
            .ThenBy(x => x.User!.DisplayName, StringComparer.Ordinal)
            .Select(x => new ParticipantDto(
                x.UserId,
                x.User!.DisplayName,
                ParticipantStatusCodes.ToCode(x.Status),
                x.IsOrganizer,
                x.User.AvatarColorIndex))
            .ToList();

        var artifacts = source.Artifacts
            .OrderBy(x => x.SortOrder)
            .Select(x => new ArtifactDto(
                x.Id,
                ArtifactKindCodes.ToCode(x.Kind),
                x.Title,
                x.Subtitle,
                x.Url))
            .ToList();

        return new EventDetailsDto(ToDto(occurrence, viewerId), source.Description, participants, artifacts);
    }

    public static ArtifactDto ToDto(EventArtifact artifact) =>
        new(artifact.Id, ArtifactKindCodes.ToCode(artifact.Kind), artifact.Title, artifact.Subtitle, artifact.Url);
}
