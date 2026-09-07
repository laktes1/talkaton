namespace Talkaton.Domain.Entities;

/// <summary>
/// Пользователь. На этапе 2 авторизации нет: вход по имени, имя уникально —
/// вернувшись с тем же именем, человек попадает ровно в свои же данные.
/// На этапе 6 сюда придёт учётка из ITalkGateway, а <see cref="NormalizedName"/> уйдёт.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Имя, как его ввёл человек и как оно показывается в списке участников.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Ключ входа: <see cref="DisplayName"/> без регистра и краевых пробелов. Уникален.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>Таймзона IANA, например "Asia/Yekaterinburg". Время событий храним в UTC.</summary>
    public string TimeZoneId { get; set; } = "Asia/Yekaterinburg";

    /// <summary>Индекс палитры аватара 0..5 — сами цвета живут во фронте, в токенах темы.</summary>
    public int AvatarColorIndex { get; set; }

    /// <summary>
    /// Резервное время до/после встречи (Этап 7.5) в минутах — своя «подушка», которую
    /// нельзя занять другой встречей впритык. Учитывается в гриде занятости
    /// (см. AvailabilityEndpoints) и на публичной странице самозаписи.
    /// </summary>
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<Calendar> Calendars { get; set; } = new List<Calendar>();

    /// <summary>Приводит введённое имя к ключу входа. Пустое имя отсекается на границе API.</summary>
    public static string Normalize(string displayName) => displayName.Trim().ToLowerInvariant();
}
