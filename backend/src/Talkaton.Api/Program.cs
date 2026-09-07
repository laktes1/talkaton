using Microsoft.EntityFrameworkCore;
using Talkaton.Api.Availability;
using Talkaton.Api.Calendars;
using Talkaton.Api.Common;
using Talkaton.Api.Delegations;
using Talkaton.Api.Events;
using Talkaton.Api.Health;
using Talkaton.Api.ParticipantLists;
using Talkaton.Api.Rooms;
using Talkaton.Api.Session;
using Talkaton.Api.Users;
using Talkaton.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "TalkatonWeb";

builder.Services.AddDbContext<TalkatonDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")));

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .WithExposedHeaders(CurrentUser.HeaderName)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await DatabaseStartup.ApplyAsync(app);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Корня у API нет, и голый 404 на localhost:5080 читается как «ничего не запустилось».
    // В разработке отправляем на Swagger — это единственная осмысленная страница бэкенда.
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.UseCors(CorsPolicy);

app.MapHealthEndpoints();
app.MapSessionEndpoints();
app.MapCalendarEndpoints();
app.MapEventEndpoints();
app.MapUserEndpoints();
app.MapParticipantListEndpoints();
app.MapAvailabilityEndpoints();
app.MapRoomEndpoints();
app.MapDelegationEndpoints();

app.Run();

/// <summary>Точка входа видна тестам через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
