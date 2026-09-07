using System.Net;
using System.Net.Http.Json;
using Talkaton.Api.Calendars;
using Talkaton.Api.Common;
using Talkaton.Api.Delegations;
using Talkaton.Api.Events;

namespace Talkaton.Api.Tests;

public class DelegationApiTests(TalkatonApiFactory factory) : IClassFixture<TalkatonApiFactory>
{
    [Fact]
    public async Task Без_делегирования_создать_встречу_от_чужого_имени_нельзя()
    {
        var assistant = await factory.SignInAsync("Ассистент Без Права");
        var boss = await factory.SignInAsync("Руководитель Без Права");
        var bossId = await UserIdAsync(boss);
        var bossCalendar = await FirstCalendarAsync(boss);
        var start = CleanMonday().AddHours(9);

        var response = await assistant.PostAsJsonAsync("/api/events", new
        {
            calendarId = bossCalendar.Id,
            title = "Без разрешения",
            startUtc = start,
            endUtc = start.AddHours(1),
            onBehalfOfUserId = bossId,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task После_делегирования_ассистент_создаёт_встречу_от_имени_руководителя()
    {
        var assistant = await factory.SignInAsync("Ассистент С Правом");
        var assistantId = await UserIdAsync(assistant);
        var boss = await factory.SignInAsync("Руководитель С Правом");
        var bossId = await UserIdAsync(boss);
        var bossCalendar = await FirstCalendarAsync(boss);

        await GrantAsync(boss, assistantId);

        var start = CleanMonday().AddDays(1).AddHours(10);
        var response = await assistant.PostAsJsonAsync("/api/events", new
        {
            calendarId = bossCalendar.Id,
            title = "Встреча от имени руководителя",
            startUtc = start,
            endUtc = start.AddHours(1),
            onBehalfOfUserId = bossId,
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<EventDetailsDto>();

        Assert.Equal(bossId, created!.Occurrence.OrganizerId);
        Assert.Equal(assistantId, created.Occurrence.CreatedByUserId);
        Assert.True(created.Occurrence.IsOrganizer, "Ответ мапится с точки зрения руководителя — он организатор.");

        // Встреча реально видна в календаре руководителя.
        var (from, to) = (start.Date, start.Date.AddDays(1));
        var week = await boss.GetFromJsonAsync<List<OccurrenceDto>>($"/api/events?from={from:O}&to={to:O}");
        Assert.Contains(week!, x => x.EventId == created.Occurrence.EventId);
    }

    [Fact]
    public async Task Делегат_может_редактировать_и_удалять_встречу_руководителя()
    {
        var assistant = await factory.SignInAsync("Ассистент Редактор");
        var assistantId = await UserIdAsync(assistant);
        var boss = await factory.SignInAsync("Руководитель Редактируемый");
        var bossId = await UserIdAsync(boss);
        var bossCalendar = await FirstCalendarAsync(boss);

        await GrantAsync(boss, assistantId);

        var start = CleanMonday().AddDays(2).AddHours(11);
        var createResponse = await assistant.PostAsJsonAsync("/api/events", new
        {
            calendarId = bossCalendar.Id,
            title = "Черновик темы",
            startUtc = start,
            endUtc = start.AddHours(1),
            onBehalfOfUserId = bossId,
        });
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<EventDetailsDto>())!;

        var patchResponse = await assistant.PatchAsJsonAsync(
            $"/api/events/{created.Occurrence.EventId}",
            new { title = "Финальная тема" });
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var deleteResponse = await assistant.DeleteAsync($"/api/events/{created.Occurrence.EventId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task После_отзыва_права_ассистент_больше_не_может_создавать_от_имени_руководителя()
    {
        var assistant = await factory.SignInAsync("Ассистент Отозванный");
        var assistantId = await UserIdAsync(assistant);
        var boss = await factory.SignInAsync("Руководитель Отзывающий");
        var bossId = await UserIdAsync(boss);
        var bossCalendar = await FirstCalendarAsync(boss);

        await GrantAsync(boss, assistantId);
        var revoke = await boss.DeleteAsync($"/api/delegations/{assistantId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var start = CleanMonday().AddDays(3).AddHours(9);
        var response = await assistant.PostAsJsonAsync("/api/events", new
        {
            calendarId = bossCalendar.Id,
            title = "После отзыва",
            startUtc = start,
            endUtc = start.AddHours(1),
            onBehalfOfUserId = bossId,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Списки_делегирования_видны_обеим_сторонам()
    {
        var assistant = await factory.SignInAsync("Ассистент В Списке");
        var assistantId = await UserIdAsync(assistant);
        var boss = await factory.SignInAsync("Руководитель В Списке");
        var bossId = await UserIdAsync(boss);

        await GrantAsync(boss, assistantId);

        var myDelegates = await boss.GetFromJsonAsync<List<DelegationPersonDto>>("/api/delegations/my-delegates");
        Assert.Contains(myDelegates!, x => x.UserId == assistantId);

        var grantedToMe = await assistant.GetFromJsonAsync<List<DelegationPersonDto>>("/api/delegations/granted-to-me");
        Assert.Contains(grantedToMe!, x => x.UserId == bossId);
    }

    private static async Task GrantAsync(HttpClient boss, Guid delegateUserId)
    {
        var response = await boss.PostAsJsonAsync("/api/delegations", new { delegateUserId });
        response.EnsureSuccessStatusCode();
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
