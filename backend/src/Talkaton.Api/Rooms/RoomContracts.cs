using Talkaton.Api.Availability;
using Talkaton.Domain.Entities;

namespace Talkaton.Api.Rooms;

public record RoomDto(Guid Id, string Name, int Capacity)
{
    public static RoomDto From(Room room) => new(room.Id, room.Name, room.Capacity);
}

/// <summary>Занятость одной переговорки за запрошенный период — тот же грид, что и для людей.</summary>
public record RoomAvailabilityDto(Guid RoomId, IReadOnlyList<BusyBlockDto> BusyBlocks);
