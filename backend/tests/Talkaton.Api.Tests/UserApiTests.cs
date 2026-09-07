using System.Net;
using System.Net.Http.Json;
using Talkaton.Api.Common;

namespace Talkaton.Api.Tests;

public class UserApiTests(TalkatonApiFactory factory) : IClassFixture<TalkatonApiFactory>
{
    [Fact]
    public async Task Профиль_по_id_отдаёт_нужные_поля()
    {
        var client = await factory.SignInAsync("Профиль Для Букинга");
        var me = await client.GetFromJsonAsync<UserDto>("/api/session");

        var response = await client.GetAsync($"/api/users/{me!.Id}");

        response.EnsureSuccessStatusCode();
        var found = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(me.DisplayName, found!.DisplayName);
        Assert.Equal(me.TimeZoneId, found.TimeZoneId);
    }

    [Fact]
    public async Task Неизвестный_id_отдаёт_404()
    {
        var client = await factory.SignInAsync("Ищет Несуществующий Профиль");

        var response = await client.GetAsync($"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
