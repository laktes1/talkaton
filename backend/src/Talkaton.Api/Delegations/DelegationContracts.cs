namespace Talkaton.Api.Delegations;

/// <summary>Одна сторона делегирования — либо владелец, либо делегат, смотря какой список.</summary>
public record DelegationPersonDto(Guid UserId, string DisplayName);

public record GrantDelegationRequest(Guid DelegateUserId);
