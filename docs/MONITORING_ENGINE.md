# Monitoring engine

## Runtime

The API hosts a background service that runs a reconciliation cycle every Monitoring:PollIntervalSeconds seconds (default: 30, minimum: 5, maximum: 3600). Each cycle creates a new dependency-injection scope and SQLite DbContext.

The cycle:

1. Loads enabled rules and their schedule times.
2. Plans expected files for the current local business date and the following date.
3. Inserts only occurrences not already persisted.
4. Scans each configured folder (top-level only), including UNC paths accessible to the API process identity.
5. Persists newly discovered file versions.
6. Waits for the configured stability period and rechecks file metadata before qualifying a file.
7. Matches stable files to the oldest outstanding expected occurrence with the same filename.
8. Refreshes expected statuses and persists an execution record.

The scanner is read-only: it does not move, rename, delete, or write inbound files. A periodic scan is the source of truth; no FileSystemWatcher event is required.

## Configuration

The appsettings configuration can contain:

    "Monitoring": { "PollIntervalSeconds": 30 }

A rule FolderPath can be a local path or UNC path (for example, \\\\fileserver\\inbound\\citi). The Windows identity running the API must have list/read access to the share and its NTFS permissions.

## Status semantics

- Scheduled: the expected time has not yet arrived.
- Waiting: expected time reached, grace period still active.
- Missing: grace period expired without a matched file.
- Received: a matching file was detected within the grace period.
- Late: a matching file was detected after the grace period.
- Invalid: a matching expected filename has a disallowed extension.

Arrival time is the first scan detection time, not the file last-write timestamp. A file is only matched after its size and last-write metadata remain unchanged for the configured stability period.

## Current MVP boundaries

- Exact-name and filename-template planning are supported by the existing planner.
- Pattern and quantity modes intentionally do not invent expected filenames. Count-based shortfall reporting is a later increment.
- Scanning is top-level only; recursive subfolder support is not enabled.
- The current matcher handles expected names and extensions. Content validation, checksums, duplicate classification, holiday calendars, business-date rollover, and FileSystemWatcher acceleration remain later increments.
- Folder disappearance is recorded as one open incident and resolved when the folder becomes accessible again.
