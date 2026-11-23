namespace FileFlowMonitor.Domain.Entities;

public sealed class MonitoringIncidentWorkflow
{
    private MonitoringIncidentWorkflow() { }

    public MonitoringIncidentWorkflow(Guid incidentId, DateTimeOffset nowUtc)
    {
        if (incidentId == Guid.Empty) throw new ArgumentException("Incident id is required.", nameof(incidentId));
        MonitoringIncidentId = incidentId;
        Status = "Open";
        Severity = "Medium";
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid MonitoringIncidentId { get; private set; }
    public string Status { get; private set; } = "Open";
    public string Severity { get; private set; } = "Medium";
    public string? AssignedTo { get; private set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public string? ResolutionComment { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string? status, string? severity, string? assignedTo, string? resolutionComment, DateTimeOffset nowUtc)
    {
        if (status is not null)
        {
            var next = status.Trim();
            if (next is not ("Open" or "InProgress" or "Resolved" or "Closed"))
                throw new ArgumentException("Status must be Open, InProgress, Resolved or Closed.", nameof(status));
            var previousStatus = Status;
            if (previousStatus == "Closed" && next != "Open")
                throw new InvalidOperationException("A closed incident can only be reopened.");
            if (previousStatus == "Resolved" && next == "InProgress")
                throw new InvalidOperationException("Reopen a resolved incident before moving it back to InProgress.");
            Status = next;
            if (next == "InProgress" && !AcknowledgedAtUtc.HasValue) AcknowledgedAtUtc = nowUtc;
            if (next == "Resolved") ResolvedAtUtc ??= nowUtc;
            if (next == "Closed")
            {
                ResolvedAtUtc ??= nowUtc;
                ClosedAtUtc ??= nowUtc;
            }
            if (next == "Open" && previousStatus != "Open")
            {
                ResolvedAtUtc = null;
                ClosedAtUtc = null;
                AcknowledgedAtUtc = null;
            }
        }

        if (severity is not null)
        {
            var next = severity.Trim();
            if (next is not ("Low" or "Medium" or "High" or "Critical"))
                throw new ArgumentException("Severity must be Low, Medium, High or Critical.", nameof(severity));
            Severity = next;
        }

        if (assignedTo is not null)
        {
            var value = assignedTo.Trim();
            if (value.Length > 150) throw new ArgumentException("AssignedTo cannot exceed 150 characters.", nameof(assignedTo));
            AssignedTo = value.Length == 0 ? null : value;
        }

        if (resolutionComment is not null)
        {
            var value = resolutionComment.Trim();
            if (value.Length > 2000) throw new ArgumentException("Resolution comment cannot exceed 2000 characters.", nameof(resolutionComment));
            ResolutionComment = value.Length == 0 ? null : value;
        }

        UpdatedAtUtc = nowUtc;
    }
}
