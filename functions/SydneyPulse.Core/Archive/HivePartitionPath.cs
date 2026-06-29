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

    // Parse: inverse of ForHour — turn a Hive partition path back into the
    // DateTimeOffset that marks the START of that hour (UTC).
    // Example: "yyyy=2026/MM=06/dd=05/HH=09" → 2026-06-05T09:00:00+00:00
    //
    // Used by ArchiverFlushFunction to decide whether a partition's hour has
    // closed past the grace window. Throws on malformed input — we'd rather
    // crash the flush tick loudly than silently skip a real partition.
    public static DateTimeOffset Parse(string partitionPath)
    {
        var parts = partitionPath.Split('/');
        if (parts.Length != 4)
            throw new ArgumentException(
                $"Invalid Hive partition path: '{partitionPath}'. Expected 4 segments.");

        return new DateTimeOffset(

            // range starting at index 5 (yyyy= is 5) and going to the end of the string.
            year: int.Parse(parts[0]["yyyy=".Length..]),

            month: int.Parse(parts[1]["MM=".Length..]),
            day: int.Parse(parts[2]["dd=".Length..]),
            hour: int.Parse(parts[3]["HH=".Length..]),
            minute: 0,
            second: 0,
            offset: TimeSpan.Zero);
    }
}
