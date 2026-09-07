namespace Talkaton.Domain.Entities;

/// <summary>
/// Право «DelegateId управляет календарём OwnerId» (Этап 7.6) — как секретарь и
/// руководитель. Даёт доступ ко всем календарям владельца сразу, не к одному конкретному:
/// так же устроено делегирование в Google Calendar и Exchange, на которые мы ориентировались.
/// </summary>
public class Delegation
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public Guid DelegateId { get; set; }
    public User? Delegate { get; set; }

    public DateTime CreatedUtc { get; set; }
}
