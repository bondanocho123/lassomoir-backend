using Lassommoir.Domain;
using Lassommoir.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Infrastructure.Persistence;

public class LassommoirDbContext(DbContextOptions<LassommoirDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<MobileClient> MobileClients => Set<MobileClient>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<StageRun> StageRuns => Set<StageRun>();
    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();
    public DbSet<Command> Commands => Set<Command>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveConversion<GuidToTextConverter>();
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeToTextConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // All FKs use Restrict to prevent cascade deletes that could bypass append-only constraints.
        modelBuilder.Entity<Device>(b =>
        {
            b.ToTable("Devices");
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).IsRequired().HasMaxLength(200);
            b.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            b.HasIndex(e => e.TokenHash).IsUnique();
        });

        modelBuilder.Entity<MobileClient>(b =>
        {
            b.ToTable("MobileClients");
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).IsRequired().HasMaxLength(200);
            b.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            b.HasOne<Device>().WithMany().HasForeignKey(e => e.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => e.TokenHash).IsUnique();
        });

        modelBuilder.Entity<Project>(b =>
        {
            b.ToTable("Projects");
            b.HasKey(e => e.Id);
            b.Property(e => e.LocalId).IsRequired().HasMaxLength(200);
            b.Property(e => e.Title).IsRequired().HasMaxLength(200);
            b.HasOne<Device>().WithMany().HasForeignKey(e => e.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.DeviceId, e.LocalId }).IsUnique();
        });

        modelBuilder.Entity<TaskItem>(b =>
        {
            b.ToTable("Tasks");
            b.HasKey(e => e.Id);
            b.Property(e => e.LocalId).IsRequired().HasMaxLength(64);
            b.Property(e => e.Stage).IsRequired().HasMaxLength(32);
            b.Property(e => e.Category).IsRequired().HasMaxLength(100);
            b.Property(e => e.Title).IsRequired().HasMaxLength(500);
            b.Property(e => e.State).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.RunState).HasConversion<string>().HasMaxLength(32);
            b.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.ProjectId, e.LocalId }).IsUnique();
        });

        modelBuilder.Entity<StageRun>(b =>
        {
            b.ToTable("StageRuns");
            b.HasKey(e => e.Id);
            b.Property(e => e.Stage).IsRequired().HasMaxLength(32);
            b.Property(e => e.Outcome).IsRequired().HasMaxLength(100);
            b.Property(e => e.SessionId).IsRequired().HasMaxLength(100);
            b.Property(e => e.Decision).HasConversion<string>().HasMaxLength(32);
            b.HasOne<TaskItem>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.TaskId, e.Sequence }).IsUnique();
        });

        modelBuilder.Entity<TaskEvent>(b =>
        {
            b.ToTable("TaskEvents");
            b.HasKey(e => e.Id);
            b.Property(e => e.Type).IsRequired().HasMaxLength(100);
            b.HasOne<TaskItem>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.TaskId, e.CreatedAt });
        });

        modelBuilder.Entity<Command>(b =>
        {
            b.ToTable("Commands");
            b.HasKey(e => e.Id);
            b.Property(e => e.Type).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.State).HasConversion<string>().HasMaxLength(32);
            b.HasOne<Device>().WithMany().HasForeignKey(e => e.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TaskItem>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.DeviceId, e.State });
        });
    }
}
