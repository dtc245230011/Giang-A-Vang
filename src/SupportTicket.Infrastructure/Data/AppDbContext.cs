using Microsoft.EntityFrameworkCore;
using SupportTicket.Infrastructure.Entities;

namespace SupportTicket.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketHistory> TicketHistories => Set<TicketHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");
            entity.HasIndex(x => new { x.Role, x.IsActive });
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("Teams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");
        });

        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.ToTable("TeamMembers");
            entity.HasKey(x => new { x.TeamId, x.UserId });
            entity.Property(x => x.JoinedAt).HasColumnType("datetime2(3)");
            entity.HasOne(x => x.Team)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany(x => x.TeamMemberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.ToTable("Tickets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TicketNumber).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.TicketNumber).IsUnique();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)");
            entity.Property(x => x.ResolvedAt).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion();

            entity.HasOne(x => x.Requester)
                .WithMany(x => x.RequestedTickets)
                .HasForeignKey(x => x.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AssignedTeam)
                .WithMany(x => x.Tickets)
                .HasForeignKey(x => x.AssignedTeamId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.AssignedUser)
                .WithMany(x => x.AssignedTickets)
                .HasForeignKey(x => x.AssignedUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => new { x.Status, x.AssignedTeamId });
            entity.HasIndex(x => new { x.RequesterId, x.CreatedAt });
            entity.HasIndex(x => new { x.AssignedUserId, x.CreatedAt });
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<TicketHistory>(entity =>
        {
            entity.ToTable("TicketHistories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FieldChanged).HasMaxLength(40).IsRequired();
            entity.Property(x => x.OldValue).HasMaxLength(200);
            entity.Property(x => x.NewValue).HasMaxLength(200);
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.ChangedAt).HasColumnType("datetime2(3)");
            entity.HasOne(x => x.Ticket)
                .WithMany(x => x.Histories)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ChangedByUser)
                .WithMany(x => x.TicketHistories)
                .HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.TicketId, x.ChangedAt });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.RoutingKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.OccurredAt).HasColumnType("datetime2(3)");
            entity.Property(x => x.ProcessedAt).HasColumnType("datetime2(3)");
            entity.Property(x => x.NextAttemptAt).HasColumnType("datetime2(3)");
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("InboxMessages");
            entity.HasKey(x => x.MessageId);
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProcessedAt).HasColumnType("datetime2(3)");
            entity.HasIndex(x => x.ProcessedAt);
        });
    }
}
