using Microsoft.EntityFrameworkCore;
using Talkaton.Api.Common;
using Talkaton.Domain.Entities;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.Delegations;

/// <summary>
/// Делегирование (Этап 7.6): «этот человек может управлять моим календарём от моего имени» —
/// как секретарь и руководитель. Право не на один календарь, а на все календари владельца сразу.
/// </summary>
public static class DelegationEndpoints
{
    public static IEndpointRouteBuilder MapDelegationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/delegations")
            .AddEndpointFilter<RequireUserFilter>()
            .WithTags("Delegations");

        group.MapGet("/my-delegates", GetMyDelegatesAsync)
            .WithName("GetMyDelegates")
            .WithSummary("Кому я разрешил управлять моим календарём")
            .Produces<IReadOnlyList<DelegationPersonDto>>();

        group.MapGet("/granted-to-me", GetGrantedToMeAsync)
            .WithName("GetGrantedToMe")
            .WithSummary("От чьего имени я могу создавать и править встречи")
            .Produces<IReadOnlyList<DelegationPersonDto>>();

        group.MapPost("/", GrantAsync)
            .WithName("GrantDelegation")
            .WithSummary("Разрешить человеку управлять моим календарём")
            .Produces<DelegationPersonDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/{delegateUserId:guid}", RevokeAsync)
            .WithName("RevokeDelegation")
            .WithSummary("Забрать право управлять моим календарём")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static async Task<IResult> GetMyDelegatesAsync(CurrentUser currentUser, TalkatonDbContext db, CancellationToken ct)
    {
        var owner = currentUser.Required;
        var delegates = await db.Delegations
            .Where(x => x.OwnerId == owner.Id)
            .Include(x => x.Delegate)
            .Select(x => new DelegationPersonDto(x.DelegateId, x.Delegate!.DisplayName))
            .ToListAsync(ct);

        return Results.Ok(delegates);
    }

    private static async Task<IResult> GetGrantedToMeAsync(CurrentUser currentUser, TalkatonDbContext db, CancellationToken ct)
    {
        var viewer = currentUser.Required;
        var owners = await db.Delegations
            .Where(x => x.DelegateId == viewer.Id)
            .Include(x => x.Owner)
            .Select(x => new DelegationPersonDto(x.OwnerId, x.Owner!.DisplayName))
            .ToListAsync(ct);

        return Results.Ok(owners);
    }

    private static async Task<IResult> GrantAsync(
        GrantDelegationRequest request,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var owner = currentUser.Required;

        if (request.DelegateUserId == owner.Id)
        {
            return Results.Problem(
                title: "Нельзя делегировать самому себе",
                detail: "Выберите другого человека.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var delegateUser = await db.Users.FirstOrDefaultAsync(x => x.Id == request.DelegateUserId, ct);
        if (delegateUser is null)
        {
            return Results.Problem(
                title: "Неизвестный человек",
                detail: "Такого пользователя нет.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var existing = await db.Delegations
            .FirstOrDefaultAsync(x => x.OwnerId == owner.Id && x.DelegateId == request.DelegateUserId, ct);
        if (existing is null)
        {
            db.Delegations.Add(new Delegation
            {
                Id = Guid.NewGuid(),
                OwnerId = owner.Id,
                DelegateId = request.DelegateUserId,
                CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        return Results.Created(
            $"/api/delegations/{request.DelegateUserId}",
            new DelegationPersonDto(delegateUser.Id, delegateUser.DisplayName));
    }

    private static async Task<IResult> RevokeAsync(
        Guid delegateUserId,
        CurrentUser currentUser,
        TalkatonDbContext db,
        CancellationToken ct)
    {
        var owner = currentUser.Required;
        var existing = await db.Delegations
            .FirstOrDefaultAsync(x => x.OwnerId == owner.Id && x.DelegateId == delegateUserId, ct);

        if (existing is not null)
        {
            db.Delegations.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }
}
