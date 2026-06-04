// ParquetArchiveWriter.cs
// -----------------------
// Concrete Parquet.NET implementation of IParquetArchiveWriter (ADR-0012).
//
// Three-step pipeline:
//   1. BuildSchema()    — defines column types matching ArchiveEvent fields
//   2. BuildColumns()   — converts the input list into ParquetColumn objects
//   3. WriteAsync()     — orchestrates schema + columns into a single Parquet file
//
// Parquet.NET API note: a "row group" is a horizontal slice of a Parquet file.
// For our batch size (5min or 10K events) writing one row group per call is
// fine — keeps the writer code simple. If batches grow much beyond ~100K rows,
// split into multiple row groups for better query performance downstream.
//
// New to Parquet? The mental model:
//   - Schema = column names + types + nullability, embedded in file footer.
//   - Columns = arrays of values, one array per column, all same length.
//   - Row group = a chunk holding a contiguous set of rows for all columns.

using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace SydneyPulse.Core.Archive;

public sealed class ParquetArchiveWriter : IParquetArchiveWriter
{
    // BuildSchema: declarative schema for ArchiveEvent.
    // Column order MUST match BuildColumns output order — Parquet binds columns
    // to schema fields by ordinal, not by name.
    // Nullable fields use DataField<T?> (Parquet stores definition levels for nulls).
    // Estimated implementation time: 10 min.
    private static ParquetSchema BuildSchema()
    {
        throw new NotImplementedException();
    }

    // BuildColumns: pivots a list of ArchiveEvent records into per-column arrays.
    // For each ArchiveEvent field, produces one DataColumn whose array length equals events.Count.
    // Nullable columns use T?[] arrays; non-nullable use T[].
    // Estimated implementation time: 20 min.
    private static IReadOnlyList<DataColumn> BuildColumns(
        IReadOnlyList<ArchiveEvent> events,
        ParquetSchema schema)
    {
        throw new NotImplementedException();
    }

    // WriteAsync: open ParquetWriter on destination, write one row group, dispose.
    // Disposing ParquetWriter finalises the file footer with schema metadata —
    // a Parquet file is unreadable until the writer disposes cleanly.
    // Caller owns the stream (open + close + dispose).
    // Estimated implementation time: 15 min.
    public async Task WriteAsync(
        Stream destination,
        IReadOnlyList<ArchiveEvent> events,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
