using System.Net.Http.Json;
using Talkaton.Api.Availability;
using Talkaton.Api.Calendars;
using Talkaton.Api.Common;
using Talkaton.Api.Events;

namespace Talkaton.Api.Tests;

public class AvailabilityApiTests(TalkatonApiFactory factory) : IClassFixture<TalkatonApiFactory>
{
    // Каждый новый пользователь при первом входе получает засеянную демо-неделю: календарь
    // текущей недели входа плюс «Штаб Платформы Данных», повторяющийся еженедельно по средам
    // НАВСЕГДА (UserWorkspaceProvisioner). Обычная дата вроде «+3 дня от сегодня» рискует
    // случайно попасть на демо-встречу или всё-дневное событие — совпадение зависит от того,
    // на какой день недели/час выпадет прогон теста. Поэтому берём заведомо чистый понедельник
    // далеко в будущем (60+ дней — вне зоны действия одноразовых демо-событий недели входа,
    // и не среда — вне зоны действия бесконечно повторяющегося «Штаба»).

    [Fact]
    public async Task Занятый_интервал_совпадает_со_временем_встречи()
    {
        var client = await factory.SignInAsync("Занятой Организатор");
        var userId = await UserIdAsync(client);
        var calendar = await FirstCalendarAsync(client);
        var start = CleanMonday().AddHours(10);

        await CreateAsync(client, calendar.Id, "Синхрон", start, start.AddHours(1));

        var availability = await GetAsync(client, [userId], start.Date, start.Date.AddDays(1));

        var mine = Assert.Single(availability, x => x.UserId == userId);
        var block = Assert.Single(mine.BusyBlocks);
        Assert.Equal(start, block.StartUtc);
        Assert.Equal(start.AddHours(1), block.EndUtc);
    }

    [Fact]
    public async Task Отклонённая_встреча_не_считается_занятостью()
    {
        var organizer = await factory.SignInAsync("Зовёт На Синхрон");
        var guest = await factory.SignInAsync("Откажется От Синхрона");
        var guestId = await UserIdAsync(guest);
        var calendar = await FirstCalendarAsync(organizer);
        var start = CleanMonday().AddDays(1).AddHours(11);

        var created = await CreateAsync(organizer, calendar.Id, "Необязательный созвон", start, start.AddHours(1), [guestId]);
        await guest.PostAsJsonAsync($"/api/events/{created.Occurrence.EventId}/rsvp", new { status = "declined" });

        var availability = await GetAsync(organizer, [guestId], start.Date, start.Date.AddDays(1));

        var mine = Assert.Single(availability, x => x.UserId == guestId);
        Assert.Empty(mine.BusyBlocks);
    }

    [Fact]
    public async Task Пересекающиеся_встречи_сливаются_в_один_интервал()
    {
        var client = await factory.SignInAsync("Плотный День");
        var userId = await UserIdAsync(client);
        var calendar = await FirstCalendarAsync(client);
        var start = CleanMonday().AddDays(3).AddHours(9); // понедельник+3 = четверг, тоже не среда

        await CreateAsync(client, calendar.Id, "Первая часть", start, start.AddHours(1.5));
        await CreateAsync(client, calendar.Id, "Продолжение внахлёст", start.AddHours(1), start.AddHours(2));

        var availability = await GetAsync(client, [userId], start.Date, start.Date.AddDays(1));

        var mine = Assert.Single(availability, x => x.UserId == userId);
        var block = Assert.Single(mine.BusyBlocks);
        Assert.Equal(start, block.StartUtc);
        Assert.Equal(start.AddHours(2), block.EndUtc);
    }

    [Fact]
    public async Task Неизвестный_пользователь_возвращается_с_пустым_списком_а_не_ошибкой()
    {
        var client = await factory.SignInAsync("Спрашивает Про Чужого");
        var unknownId = Guid.NewGuid();
        var window = CleanMonday();

        var availability = await GetAsync(client, [unknownId], window, window.AddDays(1));

        var mine = Assert.Single(availability, x => x.UserId == unknownId);
        Assert.Empty(mine.BusyBlocks);
    }

    /// <summary>
    /// Понедельник как минимум через 60 дней от сегодня — заведомо вне недели первого входа
    /// (там живут одноразовые демо-события) и не среда (там живёт бесконечный «Штаб»).
    /// </summary>
    private static DateTime CleanMonday()
    {
        var candidate = DateTime.UtcNow.Date.AddDays(60);
        while (candidate.DayOfWeek != DayOfWeek.Monday)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    private static async Task<CalendarDto> FirstCalendarAsync(HttpClient client)
    {
        var calendars = await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        return calendars!.First();
    }

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<UserDto>("/api/session");
        return me!.Id;
    }

    private static async Task<EventDetailsDto> CreateAsync(
        HttpClient client,
        Guid calendarId,
        string title,
        DateTime startUtc,
        DateTime endUtc,
        Guid[]? participantIds = null)
    {
        var response = await client.PostAsJsonAsync("/api/events", new { calendarId, title, startUtc, endUtc, participantIds });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventDetailsDto>())!;
    }

    private static async Task<List<UserAvailabilityDto>> GetAsync(HttpClient client, Guid[] userIds, DateTime from, DateTime to)
    {
        var ids = string.Join(',', userIds);
        var result = await client.GetFromJsonAsync<List<UserAvailabilityDto>>(
            $"/api/availability?userIds={ids}&from={from:O}&to={to:O}");
        return result!;
    }
}
