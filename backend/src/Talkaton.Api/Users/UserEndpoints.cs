using Microsoft.EntityFrameworkCore;
using Talkaton.Api.Common;
using Talkaton.Infrastructure.Persistence;

namespace Talkaton.Api.Users;

public static class UserEndpoints
{
    private const int MaxResults = 20;
    private const int MaxBufferMinutes = 60;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users/{id:guid}", async (
                Guid id,
                TalkatonDbContext db,
                CancellationToken ct) =>
            {
                var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
                return user is null
                    ? Results.NotFound()
                    : Results.Ok(UserDto.From(user));
            })
            .AddEndpointFilter<RequireUserFilter>()
            .WithName("GetUser")
            .WithTags("Users")
            .WithSummary("Профиль человека по id — нужен публичной странице самозаписи (Этап 7.4)")
            .Produces<UserDto>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/users", async (
                string? query,
                CurrentUser currentUser,
                TalkatonDbContext db,
                CancellationToken ct) =>
            {
                var people = db.Users.Where(x => x.Id != currentUser.Required.Id);

                if (!string.IsNullOrWhiteSpace(query))
                {
                    var needle = query.Trim().ToLowerInvariant();
                    people = people.Where(x => x.NormalizedName.Contains(needle));
                }

                var found = await people
                    .OrderBy(x => x.DisplayName)
                    .Take(MaxResults)
                    .ToListAsync(ct);

                return Results.Ok(found.Select(UserDto.From).ToList());
            })
            .AddEndpointFilter<RequireUserFilter>()
            .WithName("SearchUsers")
            .WithTags("Users")
            .WithSummary("Поиск людей для добавления в участники встречи")
            .Produces<IReadOnlyList<UserDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPatch("/api/users/me/buffer", async (
                UpdateBufferRequest request,
                CurrentUser currentUser,
                TalkatonDbContext db,
                CancellationToken ct) =>
            {
                var user = currentUser.Required;
                user.BufferBeforeMinutes = Math.Clamp(request.BufferBeforeMinutes, 0, MaxBufferMinutes);
                user.BufferAfterMinutes = Math.Clamp(request.BufferAfterMinutes, 0, MaxBufferMinutes);
                await db.SaveChangesAsync(ct);

                return Results.Ok(UserDto.From(user));
            })
            .AddEndpointFilter<RequireUserFilter>()
            .WithName("UpdateMyBuffer")
            .WithTags("Users")
            .WithSummary("Резервное время до/после встречи (Этап 7.5) — своя настройка")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}

/// <summary>Буфер длиннее часа не имеет практического смысла — обрезаем на границе API.</summary>
public record UpdateBufferRequest(int BufferBeforeMinutes, int BufferAfterMinutes);
