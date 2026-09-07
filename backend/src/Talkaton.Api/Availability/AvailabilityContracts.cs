namespace Talkaton.Api.Availability;

/// <summary>Один занятый интервал в гриде занятости — без темы встречи, только время.</summary>
public record BusyBlockDto(DateTime StartUtc, DateTime EndUtc);

/// <summary>Занятость одного участника за запрошенный период.</summary>
public record UserAvailabilityDto(Guid UserId, IReadOnlyList<BusyBlockDto> BusyBlocks);
