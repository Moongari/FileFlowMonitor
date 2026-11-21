namespace FileFlowMonitor.Domain.Entities;

public sealed class MonitoringExecution
{
    private MonitoringExecution() { }

    public MonitoringExecution(Guid monitoringRuleId, DateTimeOffset startedAtUtc)
    {
        Id = Guid.NewGuid();
        MonitoringRuleId = monitoringRuleId;
        StartedAtUtc = startedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MonitoringRuleId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int FilesDiscovered { get; private set; }
    public string? Error { get; private set; }

    public void Complete(DateTimeOffset completedAtUtc, int filesDiscovered)
    {
        if (completedAtUtc < StartedAtUtc) throw new ArgumentException("Completion cannot precede start.", nameof(completedAtUtc));
        if (filesDiscovered < 0) throw new ArgumentOutOfRangeException(nameof(filesDiscovered));
        CompletedAtUtc = completedAtUtc;
        FilesDiscovered = filesDiscovered;
    }

    public void Fail(DateTimeOffset completedAtUtc, string error)
    {
        if (completedAtUtc < StartedAtUtc) throw new ArgumentException("Completion cannot precede start.", nameof(completedAtUtc));
        CompletedAtUtc = completedAtUtc;
        Error = error;
    }
}
