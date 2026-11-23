namespace FileFlowMonitor.Domain.Entities;

public sealed class MonitoringIncidentHistory
{
    private MonitoringIncidentHistory() { }

    public MonitoringIncidentHistory(Guid incidentId, string action, string? details, DateTimeOffset occurredAtUtc)
    {
        if (incidentId == Guid.Empty) throw new ArgumentException("Incident id is required.", nameof(incidentId));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));
        if (action.Length > 80) throw new ArgumentException("Action cannot exceed 80 characters.", nameof(action));
        if (details?.Length > 2000) throw new ArgumentException("Details cannot exceed 2000 characters.", nameof(details));
        Id = Guid.NewGuid();
        MonitoringIncidentId = incidentId;
        Action = action;
        Details = details;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MonitoringIncidentId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
}
