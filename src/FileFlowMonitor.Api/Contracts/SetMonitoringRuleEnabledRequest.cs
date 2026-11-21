namespace FileFlowMonitor.Api.Contracts;

/// <summary>Paramètre de changement d'état d'une règle.</summary>
public sealed record SetMonitoringRuleEnabledRequest(bool IsEnabled);
