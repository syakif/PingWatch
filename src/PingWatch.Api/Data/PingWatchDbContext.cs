using Microsoft.EntityFrameworkCore;
using PingWatch.Api.Entities;

namespace PingWatch.Api.Data;

public class PingWatchDbContext : DbContext
{
    public PingWatchDbContext(DbContextOptions<PingWatchDbContext> options)
        : base(options)
    {
    }

    public DbSet<Entities.Monitor> Monitors => Set<Entities.Monitor>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Entities.Monitor>(entity =>
        {
            entity.ToTable("Monitors");
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(m => m.Url)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(m => m.IsActive)
                .HasDefaultValue(true);

            entity.Property(m => m.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasMany(m => m.CheckResults)
                .WithOne(cr => cr.Monitor)
                .HasForeignKey(cr => cr.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CheckResult>(entity =>
        {
            entity.ToTable("CheckResults");
            entity.HasKey(cr => cr.Id);

            entity.Property(cr => cr.ErrorMessage)
                .HasMaxLength(1000);

            entity.Property(cr => cr.CheckedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            // Composite index for fast history and latest status queries
            entity.HasIndex(cr => new { cr.MonitorId, cr.CheckedAt });
        });
    }
}
