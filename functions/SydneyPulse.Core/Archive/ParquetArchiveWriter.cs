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
using System.Security.Cryptography;
using TransitRealtime;
using static TransitRealtime.VehiclePosition.Types;

namespace SydneyPulse.Core.Archive;

public sealed class ParquetArchiveWriter : IParquetArchiveWriter
{
    // BuildSchema: declarative schema for ArchiveEvent.
    // Column order MUST match BuildColumns output order — Parquet binds columns
    // to schema fields by ordinal, not by name.
    // Nullable fields use DataField<T?> (Parquet stores definition levels for nulls).
    // Estimated implementation time: 10 min.
    internal static ParquetSchema BuildSchema()
    {
        // Identity & discrimination
        var eventId = new DataField<string>("eventId", nullable: false);
        var eventType = new DataField<string>("eventType", nullable: false);
        var eventVersion = new DataField<string>("eventVersion", nullable: false);

        // Three timestamps
        // Parquet.NET 4.x dropped DateTimeOffset support — store as DateTime (UTC).
        // BuildColumns converts each DateTimeOffset to .UtcDateTime at write time.
        var sourceTimestamp = new DataField<DateTime>("sourceTimestamp", nullable: false);
        var publishedAt = new DataField<DateTime>("publishedAt", nullable: false);
        var archivedAt = new DataField<DateTime>("archivedAt", nullable: false);

        // Routing key
        var routeShortName = new DataField<string>("routeShortName", nullable: false);

        // VehicleUpdate fields (nullable for alerts)
        var vehicleId = new DataField<string?>("vehicleId");
        var tripId = new DataField<string?>("tripId");
        var routeId = new DataField<string?>("routeId");
        var routeLongName = new DataField<string?>("routeLongName");
        var routeColor = new DataField<string?>("routeColor");
        var mode = new DataField<string?>("mode");
        var latitude = new DataField<double?>("latitude");
        var longitude = new DataField<double?>("longitude");
        var bearing = new DataField<float?>("bearing");
        var speedKmh = new DataField<float?>("speedKmh");
        var occupancyStatus = new DataField<string?>("occupancyStatus");

        // ServiceAlert fields (nullable for vehicles)
        var alertId = new DataField<string?>("alertId");
        var severity = new DataField<string?>("severity");
        var headerText = new DataField<string?>("headerText");
        var descriptionText = new DataField<string?>("descriptionText");
        var startsAt = new DataField<DateTime?>("startsAt");
        var endsAt = new DataField<DateTime?>("endsAt");

        return new ParquetSchema(
            eventId, eventType, eventVersion,
            sourceTimestamp, publishedAt, archivedAt,
            routeShortName,
            vehicleId, tripId, routeId, routeLongName, routeColor, mode,
            latitude, longitude, bearing, speedKmh, occupancyStatus,
            alertId, severity, headerText, descriptionText, startsAt, endsAt
        );
    }

    // BuildColumns: pivots a list of ArchiveEvent records into per-column arrays.
    // For each ArchiveEvent field, produces one DataColumn whose array length equals events.Count.
    // Nullable columns use T?[] arrays; non-nullable use T[].
    // Estimated implementation time: 20 min.
    internal static IReadOnlyList<DataColumn> BuildColumns(
        IReadOnlyList<ArchiveEvent> events,
        ParquetSchema schema)
    {
        // I can write this with LINQ and reflection in lesser lines,
        // but that would be inefficient
        // it would iterate over events once per field - so 24 columns = 24 loops,
        // and use reflection is slower and less readable

        // fields dictionary
        // for quick lookup by name when constructing DataColumns below
        var fieldsDictionary = schema.DataFields.ToDictionary(f => f.Name);

        // arrays for each column, matching schema order
        var eventIds = new string[events.Count];
        var eventTypes = new string[events.Count];
        var eventVersions = new string[events.Count];
        var sourceTimestamps = new DateTime[events.Count];
        var publishedAtValues = new DateTime[events.Count];
        var archivedAtValues = new DateTime[events.Count];
        var routeShortNames = new string[events.Count];
        var vehicleIds = new string?[events.Count];
        var tripIds = new string?[events.Count];
        var routeIds = new string?[events.Count];
        var routeLongNames = new string?[events.Count];
        var routeColors = new string?[events.Count];
        var modes = new string?[events.Count];
        var latitudes = new double?[events.Count];
        var longitudes = new double?[events.Count];
        var bearings = new float?[events.Count];
        var speedKmhs = new float?[events.Count];
        var occupancyStatuses = new string?[events.Count];
        var alertIds = new string?[events.Count];
        var severities = new string?[events.Count];
        var headerTexts = new string?[events.Count];
        var descriptionTexts = new string?[events.Count];
        var startsAtValues = new DateTime?[events.Count];
        var endsAtValues = new DateTime?[events.Count];


        // populate arrays by iterating over events once,
        // writing each field to the correct column array
        for (int i = 0; i < events.Count; i++)
        {
            var currEvent = events[i];
            eventIds[i] = currEvent.EventId;
            eventTypes[i] = currEvent.EventType;
            eventVersions[i] = currEvent.EventVersion;
            sourceTimestamps[i] = currEvent.SourceTimestamp.UtcDateTime;
            publishedAtValues[i] = currEvent.PublishedAt.UtcDateTime;
            archivedAtValues[i] = currEvent.ArchivedAt.UtcDateTime;
            routeShortNames[i] = currEvent.RouteShortName;
            vehicleIds[i] = currEvent.VehicleId;
            tripIds[i] = currEvent.TripId;
            routeIds[i] = currEvent.RouteId;
            routeLongNames[i] = currEvent.RouteLongName;
            routeColors[i] = currEvent.RouteColor;
            modes[i] = currEvent.Mode;
            latitudes[i] = currEvent.Latitude;
            longitudes[i] = currEvent.Longitude;
            bearings[i] = currEvent.Bearing;
            speedKmhs[i] = currEvent.SpeedKmh;
            occupancyStatuses[i] = currEvent.OccupancyStatus;
            alertIds[i] = currEvent.AlertId;
            severities[i] = currEvent.Severity;
            headerTexts[i] = currEvent.HeaderText;
            descriptionTexts[i] = currEvent.DescriptionText;
            startsAtValues[i] = currEvent.StartsAt?.UtcDateTime;
            endsAtValues[i] = currEvent.EndsAt?.UtcDateTime;
        }

        // create DataColumn for each schema field, using the corresponding array
        var eventIdsColumn = new DataColumn(fieldsDictionary["eventId"], eventIds);
        var eventTypesColumn = new DataColumn(fieldsDictionary["eventType"], eventTypes);
        var eventVersionsColumn = new DataColumn(fieldsDictionary["eventVersion"], eventVersions);
        var sourceTimestampsColumn = new DataColumn(fieldsDictionary["sourceTimestamp"], sourceTimestamps);
        var publishedAtColumn = new DataColumn(fieldsDictionary["publishedAt"], publishedAtValues);
        var archivedAtColumn = new DataColumn(fieldsDictionary["archivedAt"], archivedAtValues);
        var routeShortNamesColumn = new DataColumn(fieldsDictionary["routeShortName"], routeShortNames);
        var vehicleIdsColumn = new DataColumn(fieldsDictionary["vehicleId"], vehicleIds);
        var tripIdsColumn = new DataColumn(fieldsDictionary["tripId"], tripIds);
        var routeIdsColumn = new DataColumn(fieldsDictionary["routeId"], routeIds);
        var routeLongNamesColumn = new DataColumn(fieldsDictionary["routeLongName"], routeLongNames);
        var routeColorsColumn = new DataColumn(fieldsDictionary["routeColor"], routeColors);
        var modesColumn = new DataColumn(fieldsDictionary["mode"], modes);
        var latitudesColumn = new DataColumn(fieldsDictionary["latitude"], latitudes);
        var longitudesColumn = new DataColumn(fieldsDictionary["longitude"], longitudes);
        var bearingsColumn = new DataColumn(fieldsDictionary["bearing"], bearings);
        var speedKmhsColumn = new DataColumn(fieldsDictionary["speedKmh"], speedKmhs);
        var occupancyStatusesColumn = new DataColumn(fieldsDictionary["occupancyStatus"], occupancyStatuses);
        var alertIdsColumn = new DataColumn(fieldsDictionary["alertId"], alertIds);
        var severitiesColumn = new DataColumn(fieldsDictionary["severity"], severities);
        var headerTextsColumn = new DataColumn(fieldsDictionary["headerText"], headerTexts);
        var descriptionTextsColumn = new DataColumn(fieldsDictionary["descriptionText"], descriptionTexts);
        var startsAtColumn = new DataColumn(fieldsDictionary["startsAt"], startsAtValues);
        var endsAtColumn = new DataColumn(fieldsDictionary["endsAt"], endsAtValues);

        var columns = new DataColumn[]
        {
            eventIdsColumn, eventTypesColumn, eventVersionsColumn,
            sourceTimestampsColumn, publishedAtColumn, archivedAtColumn,
            routeShortNamesColumn,
            vehicleIdsColumn, tripIdsColumn, routeIdsColumn, routeLongNamesColumn, routeColorsColumn, modesColumn,
            latitudesColumn, longitudesColumn, bearingsColumn, speedKmhsColumn, occupancyStatusesColumn,
            alertIdsColumn, severitiesColumn, headerTextsColumn, descriptionTextsColumn, startsAtColumn, endsAtColumn
        };
        return columns;
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
        // 1. Get the schema
        var schema = BuildSchema();

        // 2. Open the writer — disposing flushes the footer. WITHOUT dispose, the file
        //    is corrupt and unreadable.
        using var writer = await ParquetWriter.CreateAsync(schema, destination,
            cancellationToken: cancellationToken);

        // 3. Create one row group (a horizontal slice — for our batch size, one is fine)
        using var rowGroup = writer.CreateRowGroup();

        // 4. Build columns and write each one
        var columns = BuildColumns(events, schema);
        foreach (var column in columns)
        {
            await rowGroup.WriteColumnAsync(column, cancellationToken);
        }
    }
}
