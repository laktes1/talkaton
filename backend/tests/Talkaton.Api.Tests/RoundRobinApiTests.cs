using System.Net;
using System.Net.Http.Json;
using Talkaton.Api.Common;
using Talkaton.Api.ParticipantLists;

namespace Talkaton.Api.Tests;

public class RoundRobinApiTests(TalkatonApiFactory factory) : IClassFixture<TalkatonApiFactory>
{
    [Fact]
    public async Task Ротация_проходит_по_всем_участникам_по_кругу()
    {
        var owner = await factory.SignInAsync("Владелец Ротации");
        var people = await owner.GetFromJsonAsync<List<UserDto>>("/api/users");
        var members = people!.Take(3).Select(x => x.Id).ToArray();

        var list = await CreateListAsync(owner, "Дежурные по ротации", members);

        var first = await NextAsync(owner, list.Id);
        var second = await NextAsync(owner, list.Id);
        var third = await NextAsync(owner, list.Id);
        var fourth = await NextAsync(owner, list.Id);

        var firstThree = new[] { first.Id, second.Id, third.Id };
        Assert.Equal(members.Order(), firstThree.Order());

        // Четвёртый вызов — снова первый по кругу: ротация не останавливается на последнем.
        Assert.Equal(first.Id, fourth.Id);
    }

    [Fact]
    public async Task Пустой_список_отклоняет_запрос_ротации()
    {
        var owner = await factory.SignInAsync("Владелец Пустой Ротации");
        var list = await CreateListAsync(owner, "Пустой список", []);

        var response = await owner.PostAsync($"/api/participant-lists/{list.Id}/round-robin/next", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Чужой_список_не_отдаёт_ротацию()
    {
        var owner = await factory.SignInAsync("Владелец Чужой Ротации");
        var stranger = await factory.SignInAsync("Не Владелец Ротации");
        var people = await owner.GetFromJsonAsync<List<UserDto>>("/api/users");
        var members = people!.Take(1).Select(x => x.Id).ToArray();
        var list = await CreateListAsync(owner, "Не твой список", members);

        var response = await stranger.PostAsync($"/api/participant-lists/{list.Id}/round-robin/next", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<ParticipantListDto> CreateListAsync(HttpClient client, string name, Guid[] memberIds)
    {
        var response = await client.PostAsJsonAsync(
            "/api/participant-lists",
            new CreateParticipantListRequest(name, "blue", memberIds));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ParticipantListDto>())!;
    }

    private static async Task<UserDto> NextAsync(HttpClient client, Guid listId)
    {
        var response = await client.PostAsync($"/api/participant-lists/{listId}/round-robin/next", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }
}
