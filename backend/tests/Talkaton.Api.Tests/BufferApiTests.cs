using System.Net.Http.Json;
using Talkaton.Api.Availability;
using Talkaton.Api.Calendars;
using Talkaton.Api.Common;
using Talkaton.Api.Events;

namespace Talkaton.Api.Tests;

public class BufferApiTests(TalkatonApiFactory factory) : IClassFixture<TalkatonApiFactory>
{
    [Fact]
    public async Task Буфер_растягивает_занятый_интервал_в_обе_стороны()
    {
        var client = await factory.SignInAsync("Настраивает Буфер");
        var userId = await UserIdAsync(client);

        var patched = await client.PatchAsJsonAsync(
            "/api/users/me/buffer",
            new { bufferBeforeMinutes = 10, bufferAfterMinutes = 15 });
        patched.EnsureSuccessStatusCode();

        var calendar = await FirstCalendarAsync(client);
        var start = CleanMonday().AddHours(10);
        await CreateAsync(client, calendar.Id, "Встреча с буфером", start, start.AddHours(1));

        var availability = await GetAvailabilityAsync(client, [userId], start.Date, start.Date.AddDays(1));

        var mine = Assert.Single(availability, x => x.UserId == userId);
        var block = Assert.Single(mine.BusyBlocks);
        Assert.Equal(start.AddMinutes(-10), block.StartUtc);
        Assert.Equal(start.AddHours(1).AddMinutes(15), block.EndUtc);
    }

    [Fact]
    public async Task Буфер_по_умолчанию_нулевой()
    {
        var client = await factory.SignInAsync("Не Трогал Буфер");
        var userId = await UserIdAsync(client);
        var calendar = await FirstCalendarAsync(client);
        var start = CleanMonday().AddDays(1).AddHours(9);

        await CreateAsync(client, calendar.Id, "Без буфера", start, start.AddHours(1));

        var availability = await GetAvailabilityAsync(client, [userId], start.Date, start.Date.AddDays(1));

        var mine = Assert.Single(availability, x => x.UserId == userId);
        var block = Assert.Single(mine.BusyBlocks);
        Assert.Equal(start, block.StartUtc);
        Assert.Equal(start.AddHours(1), block.EndUtc);
    }

    [Fact]
    public async Task Буфер_обрезается_по_верхней_границе()
    {
        var client = await factory.SignInAsync("Просит Слишком Большой Буфер");

        var response = await client.PatchAsJsonAsync(
            "/api/users/me/buffer",
            new { bufferBeforeMinutes = 999, bufferAfterMinutes = -50 });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<UserDto>();

        Assert.Equal(60, updated!.BufferBeforeMinutes);
        Assert.Equal(0, updated.BufferAfterMinutes);
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

    private static async Task CreateAsync(HttpClient client, Guid calendarId, string title, DateTime startUtc, DateTime endUtc)
    {
        var response = await client.PostAsJsonAsync("/api/events", new { calendarId, title, startUtc, endUtc });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<List<UserAvailabilityDto>> GetAvailabilityAsync(HttpClient client, Guid[] userIds, DateTime from, DateTime to)
    {
        var ids = string.Join(',', userIds);
        var result = await client.GetFromJsonAsync<List<UserAvailabilityDto>>(
            $"/api/availability?userIds={ids}&from={from:O}&to={to:O}");
        return result!;
    }

    /// <summary>Понедельник далеко в будущем — вне зоны действия демо-недели входа.</summary>
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
