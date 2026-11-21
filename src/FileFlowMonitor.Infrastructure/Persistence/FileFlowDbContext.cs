using FileFlowMonitor.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
        // SQLite cannot order or range-filter DateTimeOffset values stored as TEXT.
        // Persist UTC-normalized values as sortable integers for reliable deadlines and chronological queries.
        var dateTimeOffsetConverter = new DateTimeOffsetToBinaryConverter();
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties())
                     .Where(property => property.ClrType == typeof(DateTimeOffset) ||
                                        property.ClrType == typeof(DateTimeOffset?)))
        {
            property.SetValueConverter(dateTimeOffsetConverter);
        }

        modelBuilder.Entity<MonitoringRule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.FolderPath).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Ignore(x => x.ExpectedFileNames);
            entity.Ignore(x => x.AllowedExtensions);
            entity.Property<string>("_expectedFileNamesJson")
                .HasField("_expectedFileNamesJson")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasColumnName("ExpectedFileNamesJson")
                .IsRequired();
            entity.Property<string>("_allowedExtensionsJson")
                .HasField("_allowedExtensionsJson")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasColumnName("AllowedExtensionsJson")
                .IsRequired();
            entity.HasMany(x => x.ScheduleTimes)
                .WithOne()
                .HasForeignKey(x => x.MonitoringRuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleTime>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MonitoringRuleId, x.ExpectedTime }).IsUnique();
        });

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
