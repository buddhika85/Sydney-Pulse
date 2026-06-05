// ArchiveManifest.cs
// ------------------
// Schema for the _manifest.json file written by ArchiverFlushFunction once
// per partition hour, after Parquet files have landed (ADR-0012).
//
// Purpose: makes "is this partition queryable yet?" a single blob read for
// analytics tools. Without it, consumers would have to list the partition
// folder (slow, racy under concurrent ingest).
//
// Layout: one manifest per partition hour
//   archive/yyyy=2026/MM=06/dd=04/HH=14/_manifest.json
//
// Estimated implementation time: 10 min (records only, no logic).

namespace SydneyPulse.Core.Archive;

// Top-level manifest record. One per partition hour.
public sealed record ArchiveManifest(
    string PartitionPath,              // e.g. "yyyy=2026/MM=06/dd=04/HH=14"
    DateTimeOffset WrittenAt,          // when the Flush Function finalised this partition
    int EventCount,                    // total events across all files in the partition
    long ByteSize,                     // total Parquet bytes across all files
    DateTimeOffset FirstSourceTimestamp, // earliest source timestamp in the partition
    DateTimeOffset LastSourceTimestamp,  // latest source timestamp in the partition
    IReadOnlyList<ArchiveManifestFile> Files
);

// Per-file entry inside the manifest. One per Parquet file in the partition.
public sealed record ArchiveManifestFile(
    string FileName,                   // e.g. "events-20260604T143012.parquet"
    int EventCount,                    // rows in this file
    long ByteSize                      // Parquet bytes on disk
);
