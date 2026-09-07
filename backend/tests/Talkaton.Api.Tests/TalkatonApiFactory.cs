using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talkaton.Api.Common;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.Tests;

/// <summary>
/// Api на SQLite в памяти. Соединение держим открытым весь тест-класс: закроется —
/// база исчезнет вместе с ним. Схему накатываем теми же миграциями, что и в проде,
/// иначе тесты проверяли бы схему, которой нигде нет.
/// </summary>
public class TalkatonApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // Схему накатывает InitializeAsync — старту приложения тут делать нечего.
                ["Database:MigrateOnStartup"] = "false",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TalkatonDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<TalkatonDbContext>();
            services.AddDbContext<TalkatonDbContext>(options => options.UseSqlite(connection));
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalkatonDbContext>();
        await db.Database.MigrateAsync();
        await RoomSeeder.EnsureSeededAsync(db);
    }

    /// <summary>Явная реализация: у базового класса свой DisposeAsync с другой сигнатурой.</summary>
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    /// <summary>Клиент, представившийся по имени, — как это делает фронт после экрана входа.</summary>
    public async Task<HttpClient> SignInAsync(string name, int utcOffsetMinutes = 300)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/session",
            new { name, timeZoneId = "Asia/Yekaterinburg", utcOffsetMinutes });
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        client.DefaultRequestHeaders.Add(CurrentUser.HeaderName, user!.Id.ToString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}
