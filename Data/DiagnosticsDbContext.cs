using Microsoft.EntityFrameworkCore;
using rt_tester.Models;

namespace rt_tester.Data;

public sealed class DiagnosticsDbContext(DbContextOptions<DiagnosticsDbContext> options) : DbContext(options)
{
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<CheckRun> CheckRuns => Set<CheckRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Finding>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.Fingerprint);
            e.HasIndex(f => f.CheckRunId);
            e.Property(f => f.FeedType).HasConversion<string>();
            e.Property(f => f.Category).HasConversion<string>();
            e.Property(f => f.Severity).HasConversion<string>();
        });

        modelBuilder.Entity<CheckRun>(e =>
        {
            e.HasKey(r => r.Id);
        });
    }
}
