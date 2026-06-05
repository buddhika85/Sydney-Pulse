// ArchiveOptions.cs
// -----------------
// Strongly-typed configuration for the Archiver Functions (ADR-0012).
// Bound from configuration section "Archive" — see Program.cs DI block.
//
// App-setting mapping (double underscore = ASP.NET Core config hierarchy):
//   Archive__DataLakeAccountName    e.g. "sydpulsedlsadev" — set by compute.bicep
//   Archive__PendingContainer       defaults to "pending"
//   Archive__ArchiveContainer       defaults to "archive"
//   Archive__BatchSizeThreshold     defaults to 10000 events
//   Archive__FlushIntervalMinutes   defaults to 5  (informational — NCRONTAB is hard-coded in the Function attribute)
//   Archive__PartitionGraceMinutes  defaults to 10 (late-event tolerance window)
//
// Only DataLakeAccountName MUST be supplied by infra. Everything else has a sane default.
//
// Estimated implementation time: 5 min (POCO).

namespace SydneyPulse.Functions;

public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    // Data Lake Gen2 storage account name (hostname only — no protocol).
    // Resolved at runtime to "https://{name}.blob.core.windows.net".
    public string DataLakeAccountName { get; set; } = "";

    // Container holding in-flight JSONL append blobs awaiting flush.
    public string PendingContainer { get; set; } = "pending";

    // Container holding finalised Parquet partitions + manifests.
    public string ArchiveContainer { get; set; } = "archive";

    // Flush a partition early once its pending blob crosses this event count.
    public int BatchSizeThreshold { get; set; } = 10_000;

    // ArchiverFlushFunction informational target cadence in minutes.
    // Actual cadence is enforced by the [TimerTrigger] NCRONTAB expression.
    public int FlushIntervalMinutes { get; set; } = 5;

    // How long after an hour ends before its partition is considered closeable.
    // Allows late-arriving events (with SourceTimestamp inside the hour) to still
    // land in the right Hive partition before it's finalised.
    public int PartitionGraceMinutes { get; set; } = 10;
}
