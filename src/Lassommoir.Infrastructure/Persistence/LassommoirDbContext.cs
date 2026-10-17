using Lassommoir.Domain;
using Lassommoir.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Infrastructure.Persistence;

public class LassommoirDbContext(DbContextOptions<LassommoirDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<Review> Reviews => Set<Review>();

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
        });

        modelBuilder.Entity<TaskItem>(b =>
        {
            b.ToTable("Tasks");
            b.HasKey(e => e.Id);
            b.Property(e => e.Title).IsRequired().HasMaxLength(500);
            b.Property(e => e.State).HasConversion<string>().HasMaxLength(32);
            b.HasOne<Device>().WithMany().HasForeignKey(e => e.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.DeviceId, e.State });
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
            b.Property(e => e.Type).IsRequired().HasMaxLength(100);
            b.Property(e => e.State).HasConversion<string>().HasMaxLength(32);
            b.HasOne<TaskItem>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Device>().WithMany().HasForeignKey(e => e.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.DeviceId, e.State });
        });

        modelBuilder.Entity<Review>(b =>
        {
            b.ToTable("Reviews");
            b.HasKey(e => e.Id);
            b.Property(e => e.Verdict).HasConversion<string>().HasMaxLength(32);
            b.HasOne<TaskItem>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => e.TaskId);
        });
    }
}
