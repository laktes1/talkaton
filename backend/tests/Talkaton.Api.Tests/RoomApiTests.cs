using System.Net;
using System.Net.Http.Json;
using Talkaton.Api.Calendars;
using Talkaton.Api.Events;
using Talkaton.Api.Rooms;

namespace Talkaton.Api.Tests;

public class RoomApiTests(TalkatonApiFactory factory) : IClassFixture<TalkatonApiFactory>
{
    [Fact]
    public async Task Список_переговорок_не_пуст_и_отдаёт_вместимость()
    {
        var client = await factory.SignInAsync("Смотрит Переговорки");

        var rooms = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms");

        Assert.NotEmpty(rooms!);
        Assert.All(rooms!, x => Assert.True(x.Capacity > 0));
    }

    [Fact]
    public async Task Встреча_с_переговоркой_создаётся_и_комната_видна_в_карточке()
    {
        var client = await factory.SignInAsync("Бронирует Переговорку");
        var calendar = await FirstCalendarAsync(client);
        var room = (await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms"))!.First();
        var start = CleanMonday().AddHours(10);

        var created = await CreateAsync(client, calendar.Id, "Очная встреча", start, start.AddHours(1), room.Id);

        Assert.Equal(room.Id, created.Occurrence.RoomId);
        Assert.Equal(room.Name, created.Occurrence.RoomName);
    }

    [Fact]
    public async Task Вторая_встреча_в_ту_же_переговорку_на_то_же_время_отклоняется()
    {
        var first = await factory.SignInAsync("Занимает Переговорку Первым");
        var second = await factory.SignInAsync("Хочет Ту Же Переговорку");
        var calendar1 = await FirstCalendarAsync(first);
        var calendar2 = await FirstCalendarAsync(second);
        var room = (await first.GetFromJsonAsync<List<RoomDto>>("/api/rooms"))!.First();
        var start = CleanMonday().AddDays(1).AddHours(12);

        await CreateAsync(first, calendar1.Id, "Уже забронировано", start, start.AddHours(1), room.Id);

        var response = await second.PostAsJsonAsync("/api/events", new
        {
            calendarId = calendar2.Id,
            title = "Хочу туда же",
            startUtc = start.AddMinutes(30),
            endUtc = start.AddMinutes(90),
            roomId = room.Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Соседняя_по_времени_бронь_без_пересечения_разрешена()
    {
        var client = await factory.SignInAsync("Бронирует Впритык");
        var calendar = await FirstCalendarAsync(client);
        var room = (await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms"))!.First();
        var start = CleanMonday().AddDays(2).AddHours(9);

        await CreateAsync(client, calendar.Id, "Первая часть дня", start, start.AddHours(1), room.Id);
        var second = await CreateAsync(client, calendar.Id, "Вторая часть дня", start.AddHours(1), start.AddHours(2), room.Id);

        Assert.Equal(room.Id, second.Occurrence.RoomId);
    }

    [Fact]
    public async Task Неизвестная_переговорка_отклоняется()
    {
        var client = await factory.SignInAsync("Просит Несуществующую Комнату");
        var calendar = await FirstCalendarAsync(client);
        var start = CleanMonday().AddDays(3).AddHours(9);

        var response = await client.PostAsJsonAsync("/api/events", new
        {
            calendarId = calendar.Id,
            title = "Встреча в никуда",
            startUtc = start,
            endUtc = start.AddHours(1),
            roomId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Занятость_переговорки_видна_в_гриде_availability()
    {
        var client = await factory.SignInAsync("Смотрит Занятость Комнаты");
        var calendar = await FirstCalendarAsync(client);
        var room = (await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms"))!.First();
        var start = CleanMonday().AddDays(4).AddHours(14);

        await CreateAsync(client, calendar.Id, "Встреча с комнатой", start, start.AddHours(1), room.Id);

        var availability = await client.GetFromJsonAsync<List<RoomAvailabilityDto>>(
            $"/api/rooms/availability?roomIds={room.Id}&from={start.Date:O}&to={start.Date.AddDays(1):O}");

        var mine = Assert.Single(availability!, x => x.RoomId == room.Id);
        var block = Assert.Single(mine.BusyBlocks);
        Assert.Equal(start, block.StartUtc);
        Assert.Equal(start.AddHours(1), block.EndUtc);
    }

    private static async Task<CalendarDto> FirstCalendarAsync(HttpClient client)
    {
        var calendars = await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        return calendars!.First();
    }

    private static async Task<EventDetailsDto> CreateAsync(
        HttpClient client,
        Guid calendarId,
        string title,
        DateTime startUtc,
        DateTime endUtc,
        Guid roomId)
    {
        var response = await client.PostAsJsonAsync("/api/events", new { calendarId, title, startUtc, endUtc, roomId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventDetailsDto>())!;
    }

    /// <summary>Понедельник далеко в будущем — вне зоны действия демо-недели входа (см. AvailabilityApiTests).</summary>
    private static DateTime CleanMonday()
    {
        var candidate = DateTime.UtcNow.Date.AddDays(60);
        while (candidate.DayOfWeek != DayOfWeek.Monday)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }
}
