namespace FileFlowMonitor.Domain.Entities;

public sealed class ScheduleTime
{
    private ScheduleTime() { }

    public ScheduleTime(Guid monitoringRuleId, TimeOnly expectedTime)
    {
        Id = Guid.NewGuid();
        MonitoringRuleId = monitoringRuleId;
        ExpectedTime = expectedTime;
    }

    public Guid Id { get; private set; }
    public Guid MonitoringRuleId { get; private set; }
    public TimeOnly ExpectedTime { get; private set; }
}
