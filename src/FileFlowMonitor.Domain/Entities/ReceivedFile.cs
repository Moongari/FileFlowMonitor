namespace FileFlowMonitor.Domain.Entities;

public sealed class ReceivedFile
{
    private ReceivedFile() { }

    public ReceivedFile(Guid monitoringRuleId, string fullPath, string fileName, long sizeBytes, DateTimeOffset lastWriteTimeUtc, DateTimeOffset detectedAtUtc)
    {
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        Id = Guid.NewGuid();
        MonitoringRuleId = monitoringRuleId;
        FullPath = fullPath;
        FileName = fileName;
        SizeBytes = sizeBytes;
        LastWriteTimeUtc = lastWriteTimeUtc;
        DetectedAtUtc = detectedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MonitoringRuleId { get; private set; }
    public string FullPath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTimeOffset LastWriteTimeUtc { get; private set; }
    public DateTimeOffset DetectedAtUtc { get; private set; }
    public DateTimeOffset? StableAtUtc { get; private set; }
    public string? Sha256 { get; private set; }

    public void MarkStable(DateTimeOffset stableAtUtc, string? sha256 = null)
    {
        StableAtUtc = stableAtUtc;
        Sha256 = sha256;
    }
}
