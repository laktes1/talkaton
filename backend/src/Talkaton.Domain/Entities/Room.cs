namespace Talkaton.Domain.Entities;

/// <summary>
/// Переговорка (Этап 7.2). Список комнат общий для всех пользователей — так же, как
/// список коллег: это не персональная сущность, а общий ресурс компании.
/// </summary>
public class Room
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Вместимость в людях — по ней фильтруют при выборе комнаты.</summary>
    public int Capacity { get; set; }

    public ICollection<Event> Events { get; set; } = new List<Event>();
}
