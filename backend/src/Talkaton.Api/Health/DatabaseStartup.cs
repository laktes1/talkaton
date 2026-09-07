using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.Health;

/// <summary>
/// Миграции на старте: локальный стенд должен подниматься одной командой,
/// без ручного `dotnet ef database update`. Демо-данными наполняется не база целиком,
/// а рабочее пространство конкретного человека — при первом входе по имени.
/// </summary>
public static class DatabaseStartup
{
    public static async Task ApplyAsync(WebApplication app)
    {
        if (!app.Configuration.GetSection("Database").GetValue("MigrateOnStartup", false))
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalkatonDbContext>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseStartup));

        EnsureDirectory(db.Database.GetConnectionString());

        await db.Database.MigrateAsync();
        logger.LogInformation("Миграции применены");

        await RoomSeeder.EnsureSeededAsync(db);
    }

    /// <summary>
    /// SQLite создаст файл базы, но не создаст каталог под него. В docker-compose файл
    /// лежит в примонтированном томе, и без этой строчки первый запуск падает.
    /// </summary>
    private static void EnsureDirectory(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
