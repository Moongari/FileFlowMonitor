using FileFlowMonitor.Infrastructure.Monitoring;
using FileFlowMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace FileFlowMonitor.Api.Services;

/// <summary>
/// Watches configured rule folders and triggers a targeted reconciliation as soon as
/// a file is created, changed, or renamed. Periodic polling remains the fallback.
/// </summary>
public sealed class FileArrivalWatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<FileArrivalWatcherService> logger) : BackgroundService
{
    private readonly Dictionary<Guid, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _triggerGates = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await RefreshWatchersAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Failed to refresh file arrival watchers."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshWatchersAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FileFlowDbContext>();
        var rules = await db.MonitoringRules.AsNoTracking()
            .Where(x => x.IsEnabled)
            .Select(x => new { x.Id, x.FolderPath })
            .ToListAsync(ct);

        var current = rules.Where(x => Directory.Exists(x.FolderPath))
            .GroupBy(x => x.Id).Select(group => group.First()).ToDictionary(rule => rule.Id, rule => rule.FolderPath);

        foreach (var id in _watchers.Keys.Where(id => !current.ContainsKey(id)).ToArray())
            RemoveWatcher(id);

        foreach (var rule in rules)
        {
            if (!Directory.Exists(rule.FolderPath)) continue;
            if (_watchers.TryGetValue(rule.Id, out var existing) &&
                string.Equals(existing.Path, rule.FolderPath, StringComparison.OrdinalIgnoreCase))
                continue;

            RemoveWatcher(rule.Id);
            try
            {
                var watcher = new FileSystemWatcher(rule.FolderPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    Filter = "*",
                    EnableRaisingEvents = true
                };
                watcher.Created += (_, e) => Trigger(rule.Id, e.FullPath);
                watcher.Changed += (_, e) => Trigger(rule.Id, e.FullPath);
                watcher.Renamed += (_, e) => Trigger(rule.Id, e.FullPath);
                watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "FileSystemWatcher error for rule {RuleId} / folder {Folder}.", rule.Id, rule.FolderPath);
                _watchers[rule.Id] = watcher;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to watch folder {FolderPath} for rule {RuleId}. Periodic polling remains active.", rule.FolderPath, rule.Id);
            }
        }
    }

    private void Trigger(Guid ruleId, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _ = ReconcileAfterEventAsync(ruleId, path);
    }

    private async Task ReconcileAfterEventAsync(Guid ruleId, string path)
    {
        var gate = _triggerGates.GetOrAdd(ruleId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0)) return; // Coalesce bursts from write/rename notifications.
        try
        {
            await Task.Delay(500);
            await using var scope = scopeFactory.CreateAsyncScope();
            var cycle = scope.ServiceProvider.GetRequiredService<MonitoringEngineCycle>();
            await cycle.ExecuteAsync(CancellationToken.None, ruleId);
            logger.LogDebug("Triggered reconciliation from file system event for rule {RuleId} and file {FilePath}.", ruleId, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event-triggered reconciliation failed for rule {RuleId}.", ruleId);
        }
        finally { gate.Release(); }
    }

    private void RemoveWatcher(Guid ruleId)
    {
        if (_watchers.Remove(ruleId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        foreach (var gate in _triggerGates.Values) gate.Dispose();
        base.Dispose();
    }
}
