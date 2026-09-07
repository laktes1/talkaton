using Microsoft.EntityFrameworkCore;
using Talkaton.Domain.Entities;

namespace Talkaton.Infrastructure.Persistence;

/// <summary>
/// Переговорки — общий ресурс компании, а не персональные данные, поэтому сеются один раз
/// на всю базу, а не при входе каждого человека (см. <see cref="UserWorkspaceProvisioner"/>).
/// Для хакатона список захардкожен — своего интерфейса администрирования комнат ещё нет.
/// </summary>
public static class RoomSeeder
{
    private static readonly (string Name, int Capacity)[] DemoRooms =
    [
        ("Переговорка «Байкал»", 6),
        ("Переговорка «Иртыш»", 10),
        ("Переговорка «Обь»", 4),
        ("Телефонная будка", 1),
    ];

    public static async Task EnsureSeededAsync(TalkatonDbContext db, CancellationToken ct = default)
    {
        if (await db.Rooms.AnyAsync(ct))
        {
            return;
        }

        foreach (var (name, capacity) in DemoRooms)
        {
            db.Rooms.Add(new Room { Id = Guid.NewGuid(), Name = name, Capacity = capacity });
        }

        await db.SaveChangesAsync(ct);
    }
}
