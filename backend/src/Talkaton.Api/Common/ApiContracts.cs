using Talkaton.Domain.Entities;

namespace Talkaton.Api.Common;

/// <summary>Человек в списках участников и в поиске.</summary>
public record UserDto(
    Guid Id,
    string DisplayName,
    string TimeZoneId,
    int AvatarColorIndex,
    int BufferBeforeMinutes,
    int BufferAfterMinutes)
{
    public static UserDto From(User user) => new(
        user.Id,
        user.DisplayName,
        user.TimeZoneId,
        user.AvatarColorIndex,
        user.BufferBeforeMinutes,
        user.BufferAfterMinutes);
}

/// <summary>
/// Строковые статусы вместо чисел: API читается глазами в Swagger и не ломается
/// от перестановки значений в enum.
/// </summary>
public static class ParticipantStatusCodes
{
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Tentative = "tentative";

    public static string ToCode(ParticipantStatus status) => status switch
    {
        ParticipantStatus.Accepted => Accepted,
        ParticipantStatus.Declined => Declined,
        _ => Tentative,
    };

    public static bool TryParse(string? code, out ParticipantStatus status)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case Accepted:
                status = ParticipantStatus.Accepted;
                return true;
            case Declined:
                status = ParticipantStatus.Declined;
                return true;
            case Tentative:
                status = ParticipantStatus.Tentative;
                return true;
            default:
                status = ParticipantStatus.Tentative;
                return false;
        }
    }
}

public static class ArtifactKindCodes
{
    public static string ToCode(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Recording => "recording",
        ArtifactKind.Protocol => "protocol",
        ArtifactKind.Board => "board",
        _ => "tasks",
    };
}
