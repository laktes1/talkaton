using Microsoft.EntityFrameworkCore;
using Talkaton.Api.Common;
using Talkaton.Domain.Entities;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.ParticipantLists;

public record ParticipantListDto(Guid Id, string Name, string Color, int SortOrder, IReadOnlyList<UserDto> Members);

public record CreateParticipantListRequest(string Name, string? Color, Guid[]? MemberIds);

public static class ParticipantListEndpoints
{
    private const int MaxNameLength = 200;
    private const int MaxMembers = 100;
    private static readonly HashSet<string> AllowedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "blue", "teal", "purple", "amber", "rose",
    };

    public static IEndpointRouteBuilder MapParticipantListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/participant-lists")
            .AddEndpointFilter<RequireUserFilter>()
            .WithTags("ParticipantLists");

        group.MapGet("/", async (CurrentUser currentUser, TalkatonDbContext db, CancellationToken ct) =>
            {
                var lists = await db.ParticipantLists
                    .Where(x => x.OwnerId == currentUser.Required.Id)
                    .Include(x => x.Members)
                    .ThenInclude(x => x.User)
                    .OrderBy(x => x.SortOrder)
                    .ToListAsync(ct);

                return Results.Ok(lists.Select(Map).ToList());
            })
            .WithName("GetParticipantLists")
            .WithSummary("Блок «Списки участников» левой панели")
            .Produces<IReadOnlyList<ParticipantListDto>>();

        group.MapPost("/", async (
                CreateParticipantListRequest request,
                CurrentUser currentUser,
                TalkatonDbContext db,
                CancellationToken ct) =>
            {
                var name = request.Name?.Trim() ?? string.Empty;
                if (name.Length == 0 || name.Length > MaxNameLength)
                {
                    return Results.Problem(
                        title: $"Название списка должно содержать от 1 до {MaxNameLength} символов",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var owner = currentUser.Required;
                var memberIds = (request.MemberIds ?? []).Distinct().ToArray();
                if (memberIds.Length > MaxMembers)
                {
                    return Results.Problem(
                        title: $"В списке может быть не больше {MaxMembers} участников",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var color = request.Color?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(color) || !AllowedColors.Contains(color))
                {
                    color = "blue";
                }

                var lastOrder = await db.ParticipantLists
                    .Where(x => x.OwnerId == owner.Id)
                    .MaxAsync(x => (int?)x.SortOrder, ct) ?? -1;

                var list = new ParticipantList
                {
                    Id = Guid.NewGuid(),
                    OwnerId = owner.Id,
                    Name = name,
                    Color = color,
                    SortOrder = lastOrder + 1,
                };

                // Один параметризованный запрос не даёт подложить несуществующий ID и не создаёт N+1.
                var validMemberIds = await db.Users
                    .Where(x => x.Id != owner.Id && memberIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(ct);

                foreach (var memberId in validMemberIds)
                {
                    list.Members.Add(new ParticipantListMember { ListId = list.Id, UserId = memberId });
                }

                db.ParticipantLists.Add(list);
                await db.SaveChangesAsync(ct);

                await db.Entry(list).Collection(x => x.Members).Query().Include(x => x.User).LoadAsync(ct);
                return Results.Created($"/api/participant-lists/{list.Id}", Map(list));
            })
            .WithName("CreateParticipantList")
            .WithSummary("Кнопка «+» рядом со списками участников")
            .Produces<ParticipantListDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", async (
                Guid id,
                CurrentUser currentUser,
                TalkatonDbContext db,
                CancellationToken ct) =>
            {
                var list = await db.ParticipantLists
                    .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == currentUser.Required.Id, ct);
                if (list is null)
                {
                    return Results.NotFound();
                }

                db.ParticipantLists.Remove(list);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("DeleteParticipantList")
            .WithSummary("Удаление списка участников")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/round-robin/next", async (
                Guid id,
                CurrentUser currentUser,
                TalkatonDbContext db,
                CancellationToken ct) =>
            {
                var list = await db.ParticipantLists
                    .Include(x => x.Members)
                    .ThenInclude(x => x.User)
                    .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == currentUser.Required.Id, ct);
                if (list is null)
                {
                    return Results.NotFound();
                }

                // Стабильный порядок по UserId — не важно какой конкретно, важно, что он
                // не меняется между вызовами, иначе ротация «прыгала» бы туда-сюда.
                var ordered = list.Members
                    .Where(x => x.User is not null)
                    .OrderBy(x => x.UserId)
                    .ToList();

                if (ordered.Count == 0)
                {
                    return Results.Problem(
                        title: "В списке никого нет",
                        detail: "Добавьте хотя бы одного человека, прежде чем распределять встречи по очереди.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var lastIndex = list.LastRoundRobinMemberId is { } lastId
                    ? ordered.FindIndex(x => x.UserId == lastId)
                    : -1;
                var nextIndex = (lastIndex + 1) % ordered.Count;
                var next = ordered[nextIndex];

                list.LastRoundRobinMemberId = next.UserId;
                await db.SaveChangesAsync(ct);

                return Results.Ok(UserDto.From(next.User!));
            })
            .WithName("NextRoundRobinMember")
            .WithSummary("Ротация по очереди (Этап 7.3): кто из списка получает следующую встречу")
            .Produces<UserDto>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static ParticipantListDto Map(ParticipantList list) => new(
        list.Id,
        list.Name,
        string.IsNullOrWhiteSpace(list.Color) ? "blue" : list.Color,
        list.SortOrder,
        list.Members
            .Where(x => x.User is not null)
            .Select(x => UserDto.From(x.User!))
            .OrderBy(x => x.DisplayName, StringComparer.Ordinal)
            .ToList());
}
