namespace FileFlowMonitor.Domain.Entities;

public sealed class MonitoringIncident
{
    private MonitoringIncident() { }

    public MonitoringIncident(Guid monitoringRuleId, Guid? expectedFileId, string code, string message, DateTimeOffset occurredAtUtc)
    {
        Id = Guid.NewGuid();
        MonitoringRuleId = monitoringRuleId;
        ExpectedFileId = expectedFileId;
        Code = code;
        Message = message;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MonitoringRuleId { get; private set; }
    public Guid? ExpectedFileId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public bool IsResolved => ResolvedAtUtc.HasValue;

    public void Resolve(DateTimeOffset resolvedAtUtc)
    {
        if (resolvedAtUtc < OccurredAtUtc) throw new ArgumentException("Resolution cannot precede occurrence.", nameof(resolvedAtUtc));
        ResolvedAtUtc = resolvedAtUtc;
    }
}
