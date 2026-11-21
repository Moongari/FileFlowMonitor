using FileFlowMonitor.Domain.Enums;

namespace FileFlowMonitor.Domain.Entities;

public sealed class ExpectedFile
{
    private ExpectedFile() { }

    public ExpectedFile(Guid monitoringRuleId, string expectedFileName, DateTimeOffset expectedAtUtc, DateTimeOffset deadlineAtUtc)
    {
        if (monitoringRuleId == Guid.Empty) throw new ArgumentException("Rule id is required.", nameof(monitoringRuleId));
        if (string.IsNullOrWhiteSpace(expectedFileName)) throw new ArgumentException("Expected file name is required.", nameof(expectedFileName));
        if (deadlineAtUtc < expectedAtUtc) throw new ArgumentException("Deadline cannot be before expected time.", nameof(deadlineAtUtc));
        Id = Guid.NewGuid();
        MonitoringRuleId = monitoringRuleId;
        ExpectedFileName = expectedFileName.Trim();
        ExpectedAtUtc = expectedAtUtc;
        DeadlineAtUtc = deadlineAtUtc;
        Status = ExpectedFileStatus.Scheduled;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid MonitoringRuleId { get; private set; }
    public string ExpectedFileName { get; private set; } = string.Empty;
    public DateTimeOffset ExpectedAtUtc { get; private set; }
    public DateTimeOffset DeadlineAtUtc { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public ExpectedFileStatus Status { get; private set; }
    public Guid? ReceivedFileId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void RefreshStatus(DateTimeOffset nowUtc)
    {
        if (Status is ExpectedFileStatus.Received or ExpectedFileStatus.Late or ExpectedFileStatus.Invalid or ExpectedFileStatus.Duplicate or ExpectedFileStatus.Cancelled) return;
        Status = nowUtc < ExpectedAtUtc ? ExpectedFileStatus.Scheduled
            : nowUtc <= DeadlineAtUtc ? ExpectedFileStatus.Waiting
            : ExpectedFileStatus.Missing;
    }

    public void MarkReceived(Guid receivedFileId, DateTimeOffset receivedAtUtc)
    {
        if (receivedFileId == Guid.Empty) throw new ArgumentException("Received file id is required.", nameof(receivedFileId));
        ReceivedFileId = receivedFileId;
        ReceivedAtUtc = receivedAtUtc;
        Status = receivedAtUtc <= DeadlineAtUtc ? ExpectedFileStatus.Received : ExpectedFileStatus.Late;
    }

    public void MarkInvalid() => Status = ExpectedFileStatus.Invalid;

    public void Cancel() => Status = ExpectedFileStatus.Cancelled;
}
