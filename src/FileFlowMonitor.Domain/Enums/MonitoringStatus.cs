namespace FileFlowMonitor.Domain.Enums;

public enum ExpectedFileStatus
{
    Scheduled = 0,
    Waiting = 1,
    Missing = 2,
    Received = 3,
    Late = 4,
    Invalid = 5,
    Duplicate = 6,
    Cancelled = 7
}

public enum MatchingMode
{
    ExactName = 0,
    FileNameTemplate = 1,
    Pattern = 2,
    Quantity = 3
}

public enum SourceAvailability
{
    Unknown = 0,
    Available = 1,
    Unavailable = 2,
    AccessDenied = 3
}

[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    EveryDay = Weekdays | Saturday | Sunday
}
