using FileFlowMonitor.Domain.Entities;
using Xunit;

namespace FileFlowMonitor.UnitTests;

public sealed class MonitoringIncidentWorkflowTests
{
    [Fact]
    public void NewWorkflow_StartsOpenWithMediumSeverity()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = new MonitoringIncidentWorkflow(Guid.NewGuid(), now);

        Assert.Equal("Open", workflow.Status);
        Assert.Equal("Medium", workflow.Severity);
        Assert.Equal(now, workflow.CreatedAtUtc);
        Assert.Equal(now, workflow.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateToInProgress_AcknowledgesIncidentOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = new MonitoringIncidentWorkflow(Guid.NewGuid(), now);

        workflow.Update("InProgress", null, "Support N2", null, now.AddMinutes(2));
        var acknowledged = workflow.AcknowledgedAtUtc;
        workflow.Update(null, "High", null, null, now.AddMinutes(5));

        Assert.Equal(now.AddMinutes(2), acknowledged);
        Assert.Equal(acknowledged, workflow.AcknowledgedAtUtc);
        Assert.Equal("High", workflow.Severity);
        Assert.Equal("Support N2", workflow.AssignedTo);
    }

    [Fact]
    public void ResolveThenReopen_ClearsResolutionAndAcknowledgement()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = new MonitoringIncidentWorkflow(Guid.NewGuid(), now);
        workflow.Update("InProgress", null, null, null, now.AddMinutes(1));
        workflow.Update("Resolved", null, null, "Fichier reçu", now.AddMinutes(2));

        Assert.Equal(now.AddMinutes(2), workflow.ResolvedAtUtc);

        workflow.Update("Open", null, null, null, now.AddMinutes(3));

        Assert.Equal("Open", workflow.Status);
        Assert.Null(workflow.ResolvedAtUtc);
        Assert.Null(workflow.ClosedAtUtc);
        Assert.Null(workflow.AcknowledgedAtUtc);
    }

    [Fact]
    public void ResolvedIncident_MustBeReopenedBeforeReturningToInProgress()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = new MonitoringIncidentWorkflow(Guid.NewGuid(), now);
        workflow.Update("Resolved", null, null, null, now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            workflow.Update("InProgress", null, null, null, now.AddMinutes(2)));
    }

    [Fact]
    public void ClosedIncident_CannotBeChangedWithoutReopening()
    {
        var workflow = new MonitoringIncidentWorkflow(Guid.NewGuid(), DateTimeOffset.UtcNow);
        workflow.Update("Closed", null, null, "Terminé", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            workflow.Update("InProgress", null, null, null, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Urgent")]
    public void InvalidStatusOrSeverity_IsRejected(string value)
    {
        var workflow = new MonitoringIncidentWorkflow(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            workflow.Update(value, null, null, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            workflow.Update(null, value, null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EmptyAssignee_ClearsAssignment()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = new MonitoringIncidentWorkflow(Guid.NewGuid(), now);
        workflow.Update(null, null, "Equipe support", null, now.AddMinutes(1));
        workflow.Update(null, null, "", null, now.AddMinutes(2));

        Assert.Null(workflow.AssignedTo);
    }
}
