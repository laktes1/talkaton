using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Talkaton.Domain.Entities;

namespace Talkaton.Infrastructure.Persistence;

public class TalkatonDbContext(DbContextOptions<TalkatonDbContext> options) : DbContext(options)
{
    /// <summary>
    /// SQLite отдаёт DateTime без Kind, и дальше по коду UTC-время начинает притворяться
    /// локальным. Конвертер прибивает Kind на входе и на выходе — один раз для всей схемы.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        value => value == null ? null : value.Value.Kind == DateTimeKind.Utc ? value : value.Value.ToUniversalTime(),
        value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    public DbSet<User> Users => Set<User>();
    public DbSet<Calendar> Calendars => Set<Calendar>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventArtifact> EventArtifacts => Set<EventArtifact>();
    public DbSet<EventOccurrenceOverride> EventOccurrenceOverrides => Set<EventOccurrenceOverride>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ParticipantList> ParticipantLists => Set<ParticipantList>();
    public DbSet<ParticipantListMember> ParticipantListMembers => Set<ParticipantListMember>();
    public DbSet<ExternalAccount> ExternalAccounts => Set<ExternalAccount>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Delegation> Delegations => Set<Delegation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(x => x.Id);
            user.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            user.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
            user.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
            user.Property(x => x.BufferBeforeMinutes).HasDefaultValue(0);
            user.Property(x => x.BufferAfterMinutes).HasDefaultValue(0);
            user.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<Calendar>(calendar =>
        {
            calendar.ToTable("calendars");
            calendar.HasKey(x => x.Id);
            calendar.Property(x => x.Name).HasMaxLength(200).IsRequired();
            calendar.Property(x => x.Color).HasMaxLength(9).IsRequired();
            calendar.HasOne(x => x.Owner)
                .WithMany(x => x.Calendars)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            calendar.HasIndex(x => new { x.OwnerId, x.SortOrder });
        });

        modelBuilder.Entity<Event>(meeting =>
        {
            meeting.ToTable("events");
            meeting.HasKey(x => x.Id);
            meeting.Property(x => x.Title).HasMaxLength(300).IsRequired();
            meeting.Property(x => x.Description).HasMaxLength(4000);
            meeting.Property(x => x.RecurrenceRule).HasMaxLength(300);
            meeting.Property(x => x.TalkRoomSlug).HasMaxLength(120);
            meeting.Ignore(x => x.Duration);

            meeting.HasOne(x => x.Calendar)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.CalendarId)
                .OnDelete(DeleteBehavior.Cascade);

            // Организатора не удаляем каскадом: снести человека и потерять встречи всей команды —
            // не то поведение, которое стоит получить случайно.
            meeting.HasOne(x => x.Organizer)
                .WithMany()
                .HasForeignKey(x => x.OrganizerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Основной индекс чтения: сетка всегда спрашивает окно по календарю.
            meeting.HasIndex(x => new { x.CalendarId, x.StartUtc });

            // Переговорка — общий ресурс, не персональный: не удаляем комнату каскадом
            // вместе с чьей-то встречей, наоборот — комнату нельзя удалить, если она занята.
            meeting.HasOne(x => x.Room)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Restrict);

            meeting.HasIndex(x => new { x.RoomId, x.StartUtc });

            // Фактический автор — только для аудита, никогда не удаляем встречу каскадом
            // из-за него (это не то же самое, что владелец/организатор).
            meeting.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventParticipant>(participant =>
        {
            participant.ToTable("event_participants");
            participant.HasKey(x => new { x.EventId, x.UserId });
            participant.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

            participant.HasOne(x => x.Event)
                .WithMany(x => x.Participants)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            participant.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // «Покажи мне мои встречи» ходит именно по этому индексу.
            participant.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<EventArtifact>(artifact =>
        {
            artifact.ToTable("event_artifacts");
            artifact.HasKey(x => x.Id);
            artifact.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
            artifact.Property(x => x.Title).HasMaxLength(200).IsRequired();
            artifact.Property(x => x.Subtitle).HasMaxLength(200);
            artifact.Property(x => x.Url).HasMaxLength(500);

            artifact.HasOne(x => x.Event)
                .WithMany(x => x.Artifacts)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            artifact.HasIndex(x => new { x.EventId, x.SortOrder });
        });

        modelBuilder.Entity<EventOccurrenceOverride>(patch =>
        {
            patch.ToTable("event_occurrence_overrides");
            patch.HasKey(x => x.Id);

            patch.HasOne(x => x.Event)
                .WithMany(x => x.Overrides)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // У одного вхождения серии не может быть двух исключений.
            patch.HasIndex(x => new { x.EventId, x.OriginalStartUtc }).IsUnique();
        });

        modelBuilder.Entity<Reminder>(reminder =>
        {
            reminder.ToTable("reminders");
            reminder.HasKey(x => new { x.EventId, x.UserId });

            reminder.HasOne(x => x.Event)
                .WithMany(x => x.Reminders)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            reminder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ParticipantList>(list =>
        {
            list.ToTable("participant_lists");
            list.HasKey(x => x.Id);
            list.Property(x => x.Name).HasMaxLength(200).IsRequired();
            list.Property(x => x.Color).HasMaxLength(32).HasDefaultValue("blue");

            list.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            list.HasIndex(x => new { x.OwnerId, x.SortOrder });
        });

        modelBuilder.Entity<ParticipantListMember>(member =>
        {
            member.ToTable("participant_list_members");
            member.HasKey(x => new { x.ListId, x.UserId });

            member.HasOne(x => x.List)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.ListId)
                .OnDelete(DeleteBehavior.Cascade);

            member.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalAccount>(account =>
        {
            account.ToTable("external_accounts");
            account.HasKey(x => x.Id);
            account.Property(x => x.Provider).HasConversion<string>().HasMaxLength(16).IsRequired();
            account.Property(x => x.AccountName).HasMaxLength(320).IsRequired();
            account.Property(x => x.SyncToken).HasMaxLength(500);

            account.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            account.HasIndex(x => new { x.UserId, x.Provider, x.AccountName }).IsUnique();
        });

        modelBuilder.Entity<Room>(room =>
        {
            room.ToTable("rooms");
            room.HasKey(x => x.Id);
            room.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Delegation>(delegation =>
        {
            delegation.ToTable("delegations");
            delegation.HasKey(x => x.Id);

            delegation.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            delegation.HasOne(x => x.Delegate)
                .WithMany()
                .HasForeignKey(x => x.DelegateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Одно и то же право не выдаём дважды — идемпотентный grant проверяет по этому индексу.
            delegation.HasIndex(x => new { x.OwnerId, x.DelegateId }).IsUnique();
        });

        ApplyUtcConverters(modelBuilder);
    }

    private static void ApplyUtcConverters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(UtcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(NullableUtcConverter);
                }
            }
        }
    }
}
