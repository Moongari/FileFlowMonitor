using FileFlowMonitor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileFlowMonitor.Infrastructure.Persistence;

public sealed class FileFlowDbContext(DbContextOptions<FileFlowDbContext> options) : DbContext(options)
{
    public DbSet<MonitoringRule> MonitoringRules => Set<MonitoringRule>();
    public DbSet<ScheduleTime> ScheduleTimes => Set<ScheduleTime>();
    public DbSet<ExpectedFile> ExpectedFiles => Set<ExpectedFile>();
    public DbSet<ReceivedFile> ReceivedFiles => Set<ReceivedFile>();
    public DbSet<MonitoringIncident> MonitoringIncidents => Set<MonitoringIncident>();
    public DbSet<MonitoringExecution> MonitoringExecutions => Set<MonitoringExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MonitoringRule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.FolderPath).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Ignore(x => x.ExpectedFileNames);
            entity.Ignore(x => x.AllowedExtensions);
            entity.HasMany(x => x.ScheduleTimes).WithOne().HasForeignKey(x => x.MonitoringRuleId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ScheduleTime>().HasKey(x => x.Id);
        modelBuilder.Entity<ExpectedFile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExpectedFileName).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => new { x.MonitoringRuleId, x.ExpectedAtUtc, x.ExpectedFileName }).IsUnique();
        });
        modelBuilder.Entity<ReceivedFile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullPath).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => new { x.MonitoringRuleId, x.FullPath, x.LastWriteTimeUtc });
        });
        modelBuilder.Entity<MonitoringIncident>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        });
        modelBuilder.Entity<MonitoringExecution>().HasKey(x => x.Id);
    }
}
