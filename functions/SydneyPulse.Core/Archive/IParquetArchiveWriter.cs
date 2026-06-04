// IParquetArchiveWriter.cs
// ------------------------
// Abstraction over the Parquet write path (ADR-0012).
//
// Why an interface: ArchiverFlushFunction is a Function class — testing it
// without a real Parquet.Net dependency means mocking this contract. Same
// reasoning as ITfNswFeedClient: anything that crosses a serialisation /
// I/O boundary gets an interface so unit tests stay fast and deterministic.
//
// Estimated implementation time: 5 min (single-method contract).

namespace SydneyPulse.Core.Archive;

public interface IParquetArchiveWriter
{
    // Writes `events` to `destination` as a single Parquet file.
    // Caller owns the stream (open + close + dispose).
    // Schema is fixed — matches ArchiveEvent record shape.
    Task WriteAsync(
        Stream destination,
        IReadOnlyList<ArchiveEvent> events,
        CancellationToken cancellationToken);
}
