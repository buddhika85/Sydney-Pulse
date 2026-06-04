// HivePartitionPath.cs
// --------------------
// Builds Hive-style partition paths used by the Archiver Function (ADR-0012).
//
// Layout: "yyyy=YYYY/MM=MM/dd=DD/HH=HH"
// All values are zero-padded and UTC-normalised so Spark/Synapse partition
// pruning works without surprise.
//
// Why a dedicated helper: paths leak into multiple call sites
// (pending blob writes, Parquet output blobs, manifest writes). One source
// of truth prevents drift across the codebase.

namespace SydneyPulse.Core.Archive;

public static class HivePartitionPath
{
    // ForHour: returns the partition folder path for the hour containing `timestamp`.
    // Example: 2026-06-04T14:23:17+10:00 → "yyyy=2026/MM=06/dd=04/HH=04"
    // Note: timestamp is converted to UTC first. Always.
    // Estimated implementation time: 10 min.
    public static string ForHour(DateTimeOffset timestamp)
    {
        // Normalise to UTC first.
        timestamp = timestamp.ToUniversalTime();  

        // Format with zero-padding. Note the fixed-width components (MM, dd, HH).
        return $"yyyy={timestamp:yyyy}/MM={timestamp:MM}/dd={timestamp:dd}/HH={timestamp:HH}";
    }

    // ForFile: combines the hour-partition path with a filename.
    // Example: ForFile(ts, "events-20260604T1430.parquet")
    //          → "yyyy=2026/MM=06/dd=04/HH=04/events-20260604T1430.parquet"
    // Estimated implementation time: 5 min.
    public static string ForFile(DateTimeOffset timestamp, string fileName)
    {
        return $"{ForHour(timestamp)}/{fileName}";
    }
}
