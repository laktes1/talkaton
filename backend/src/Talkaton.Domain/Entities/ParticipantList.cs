namespace Talkaton.Domain.Entities;

/// <summary>Блок «Списки участников» из левой панели: «Команда Платформы», «Биллинг ПД».</summary>
public class ParticipantList
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public required string Name { get; set; }

    public string Color { get; set; } = "blue";

    public int SortOrder { get; set; }

    /// <summary>
    /// Ротация по очереди (Этап 7.3) — кто из списка получил встречу последним. Следующий
    /// вызов «дай следующего по очереди» берёт того, кто идёт за ним в стабильном порядке
    /// по <c>UserId</c>; <c>null</c> — ротация ещё не запускалась, начинаем с первого.
    /// </summary>
    public Guid? LastRoundRobinMemberId { get; set; }

    public ICollection<ParticipantListMember> Members { get; set; } = new List<ParticipantListMember>();
}

/// <summary>Строка списка участников. Ключ составной — (ListId, UserId).</summary>
public class ParticipantListMember
{
    public Guid ListId { get; set; }
    public ParticipantList? List { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }
}
