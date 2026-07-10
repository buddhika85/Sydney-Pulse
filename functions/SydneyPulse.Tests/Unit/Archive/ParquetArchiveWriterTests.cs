// ParquetArchiveWriterTests.cs
// ---------------------------
// Unit tests for ParquetArchiveWriter (SP1-15, ADR-0012).
// Tests schema/column helpers in isolation via InternalsVisibleTo on SydneyPulse.Core.
//
// Design decisions pinned by these tests:
//   - Column names are camelCase (consistent with Cosmos serialization across the project)
//   - 24 columns total (matches ArchiveEvent record fields one-to-one)
//   - 7 required (always present per ADR-0012), 17 nullable (event-type-specific)

using Parquet.Schema;
using SydneyPulse.Core.Archive;
using Xunit;

namespace SydneyPulse.Tests.Unit.Archive;

public class ParquetArchiveWriterTests
{
    #region Helpers
    // Helper: a fully-populated VehicleUpdate-flavoured ArchiveEvent for column tests.
    // All vehicle-specific fields set; all alert-specific fields null.
    private static ArchiveEvent MakeVehicleEvent(
        string eventId = "vu-1",
        string vehicleId = "VH-001",
        DateTimeOffset? sourceTimestamp = null) => new(
            EventId: eventId,
            EventType: "com.sydneypulse.VehicleUpdate.v1",
            EventVersion: "v1",
            SourceTimestamp: sourceTimestamp ?? new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero),
            PublishedAt: new DateTimeOffset(2026, 6, 4, 14, 30, 1, TimeSpan.Zero),
            ArchivedAt: new DateTimeOffset(2026, 6, 4, 14, 30, 2, TimeSpan.Zero),
            RouteShortName: "T1",
            VehicleId: vehicleId,
            TripId: "TRIP-1",
            RouteId: "NTH_1a",
            RouteLongName: "T1 North Shore Line",
            RouteColor: "#F99D1C",
            Mode: "sydneytrains",
            Latitude: -33.8688,
            Longitude: 151.2093,
            Bearing: 90f,
            SpeedKmh: 60f,
            OccupancyStatus: "MANY_SEATS_AVAILABLE",
            // Alert fields null for vehicle event
            AlertId: null,
            Severity: null,
            HeaderText: null,
            DescriptionText: null,
            StartsAt: null,
            EndsAt: null);

    // Helper: a fully-populated ServiceAlert-flavoured ArchiveEvent for column tests.
    // All alert-specific fields set; all vehicle-specific fields null.
    private static ArchiveEvent MakeAlertEvent(
        string eventId = "sa-1",
        string alertId = "ALERT-1") => new(
            EventId: eventId,
            EventType: "com.sydneypulse.ServiceAlert.v1",
            EventVersion: "v1",
            SourceTimestamp: new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero),
            PublishedAt: new DateTimeOffset(2026, 6, 4, 14, 30, 1, TimeSpan.Zero),
            ArchivedAt: new DateTimeOffset(2026, 6, 4, 14, 30, 2, TimeSpan.Zero),
            RouteShortName: "T1",
            // Vehicle fields null for alert event
            VehicleId: null,
            TripId: null,
            RouteId: null,
            RouteLongName: null,
            RouteColor: null,
            Mode: null,
            Latitude: null,
            Longitude: null,
            Bearing: null,
            SpeedKmh: null,
            OccupancyStatus: null,
            AlertId: alertId,
            Severity: "significant_delays",
            HeaderText: "Delays on T1",
            DescriptionText: "Significant delays expected",
            StartsAt: new DateTimeOffset(2026, 6, 4, 15, 0, 0, TimeSpan.Zero),
            EndsAt: null);
    #endregion

    #region BuildSchemaTests
    // ─── BuildSchema ────────────────────────────────────────────────────────

    // Expected schema shape (per ADR-0012 ArchiveEvent unified record).
    // Keep this list in sync with ArchiveEvent.cs if fields are added/removed.
    private static readonly string[] ExpectedFieldNames =
    [
        // Identity & discrimination (3)
        "eventId",
        "eventType",
        "eventVersion",
        // Three timestamps (3)
        "sourceTimestamp",
        "publishedAt",
        "archivedAt",
        // Routing key (1)
        "routeShortName",
        // VehicleUpdate-only fields (11)
        "vehicleId",
        "tripId",
        "routeId",
        "routeLongName",
        "routeColor",
        "mode",
        "latitude",
        "longitude",
        "bearing",
        "speedKmh",
        "occupancyStatus",
        // ServiceAlert-only fields (6)
        "alertId",
        "severity",
        "headerText",
        "descriptionText",
        "startsAt",
        "endsAt"
    ];

    // Pin the exact set of column names. Exact-set comparison catches both
    // missing fields (renames, accidental drops) and extra fields (typos,
    // experimental additions that leaked into the schema).
    [Fact]
    public void BuildSchema_HasExpectedFieldNamesInCamelCase()
    {
        // Act
        var schema = ParquetArchiveWriter.BuildSchema();

        // Assert — exact set match (24 camelCase names, no more, no less)
        var actualNames = schema.Fields.Select(f => f.Name).ToHashSet();
        var expectedNames = ExpectedFieldNames.ToHashSet();
        Assert.Equal(expectedNames, actualNames);
    }

    // Pin which columns are nullable per ADR-0012's unified-schema contract.
    // 7 fields are always populated regardless of event type; 17 are type-specific.
    // If this test fails, downstream analytics that filter on `WHERE eventType IS NOT NULL`
    // could break, or required fields could silently start landing as null.
    [Fact]
    public void BuildSchema_NullabilityMatchesArchiveEventContract()
    {
        var expectedNullability = new Dictionary<string, bool>
        {
            // ─── Required (non-nullable) — 7 fields ───
            { "eventId", false },        { "eventType", false },    { "eventVersion", false },
            { "sourceTimestamp", false },{ "publishedAt", false },  { "archivedAt", false },
            { "routeShortName", false },
            // ─── VehicleUpdate-only (nullable for alerts) — 11 fields ───
            { "vehicleId", true },       { "tripId", true },        { "routeId", true },
            { "routeLongName", true },   { "routeColor", true },    { "mode", true },
            { "latitude", true },        { "longitude", true },     { "bearing", true },
            { "speedKmh", true },        { "occupancyStatus", true },
            // ─── ServiceAlert-only (nullable for vehicles) — 6 fields ───
            { "alertId", true },         { "severity", true },      { "headerText", true },
            { "descriptionText", true }, { "startsAt", true },      { "endsAt", true }
        };

        // Act
        var schema = ParquetArchiveWriter.BuildSchema();
        var dataFields = schema.Fields.OfType<DataField>().ToDictionary(f => f.Name);

        // Assert — every expected field is present with the right nullability
        foreach (var (name, expectedNullable) in expectedNullability)
        {
            Assert.True(dataFields.ContainsKey(name),
                $"Field '{name}' missing from schema");

            var actual = dataFields[name].IsNullable;

            Assert.True(
                actual == expectedNullable,
                $"Nullability mismatch for column '{name}': expected {expectedNullable}, actual {actual}"
            );
        }
    }

    #endregion



    #region BuildColumnsTests

    // ─── BuildColumns ───────────────────────────────────────────────────────



    // Pin the structural contract: 24 columns in schema order, zero-length data
    // arrays when no events provided. Column ORDER matters because Parquet binds
    // columns to schema fields by ordinal (not by name).
    [Fact]
    public void BuildColumns_EmptyEvents_ReturnsSchemaShapedColumnsWithNoData()
    {
        // Arrange
        var schema = ParquetArchiveWriter.BuildSchema();
        var events = Array.Empty<ArchiveEvent>();

        // Act
        var columns = ParquetArchiveWriter.BuildColumns(events, schema);

        // Assert — same count and order as schema
        Assert.Equal(schema.Fields.Count, columns.Count);
        for (int i = 0; i < schema.Fields.Count; i++)
        {
            Assert.Equal(schema.Fields[i].Name, columns[i].Field.Name);
            Assert.Empty(columns[i].Data);
        }
    }

    // Pin the value-mapping contract: a mixed batch (one vehicle + one alert) lands
    // values in the right columns at the right indices, with nulls in the type-specific
    // columns that don't apply to a given event.
    [Fact]
    public void BuildColumns_MixedEvents_PopulatesCorrectColumnsForEachEventType()
    {
        // Arrange — vehicle at index 0, alert at index 1
        var vehicleEvent = MakeVehicleEvent(eventId: "vu-1", vehicleId: "VH-001");
        var alertEvent = MakeAlertEvent(eventId: "sa-1", alertId: "ALERT-1");
        var events = new[] { vehicleEvent, alertEvent };
        var schema = ParquetArchiveWriter.BuildSchema();

        // Act
        var columns = ParquetArchiveWriter.BuildColumns(events, schema);
        var byName = columns.ToDictionary(c => c.Field.Name);

        // Assert — required columns (both events have these)
        Assert.Equal(new[] { "vu-1", "sa-1" }, (string[])byName["eventId"].Data);
        Assert.Equal(
            new[] { "com.sydneypulse.VehicleUpdate.v1", "com.sydneypulse.ServiceAlert.v1" },
            (string[])byName["eventType"].Data);
        Assert.Equal(new[] { "T1", "T1" }, (string[])byName["routeShortName"].Data);

        // Vehicle-specific columns: value at index 0, null at index 1
        var vehicleIdData = (string?[])byName["vehicleId"].Data;
        Assert.Equal("VH-001", vehicleIdData[0]);
        Assert.Null(vehicleIdData[1]);

        var latitudeData = (double?[])byName["latitude"].Data;
        Assert.Equal(-33.8688, latitudeData[0]);
        Assert.Null(latitudeData[1]);

        // Alert-specific columns: null at index 0, value at index 1
        var alertIdData = (string?[])byName["alertId"].Data;
        Assert.Null(alertIdData[0]);
        Assert.Equal("ALERT-1", alertIdData[1]);

        var severityData = (string?[])byName["severity"].Data;
        Assert.Null(severityData[0]);
        Assert.Equal("significant_delays", severityData[1]);
    }

    // Critical: DateTimeOffset (domain type) must be converted to DateTime UTC
    // (storage type) at write time. Parquet.NET 4.x doesn't accept DateTimeOffset;
    // the conversion happens here, not in ArchiveEvent.
    [Fact]
    public void BuildColumns_NonUtcSourceTimestamp_ConvertsToUtcDateTime()
    {
        // Arrange — Sydney standard time (+10:00); UTC equivalent is 04:23:17
        var sydneyTime = new DateTimeOffset(2026, 6, 4, 14, 23, 17, TimeSpan.FromHours(10));
        var ev = MakeVehicleEvent(sourceTimestamp: sydneyTime);
        var schema = ParquetArchiveWriter.BuildSchema();

        // Act
        var columns = ParquetArchiveWriter.BuildColumns(new[] { ev }, schema);
        var sourceTsColumn = columns.First(c => c.Field.Name == "sourceTimestamp");

        // Assert — stored as DateTime in UTC, not Sydney local time
        var data = (DateTime[])sourceTsColumn.Data;
        Assert.Single(data);
        Assert.Equal(new DateTime(2026, 6, 4, 4, 23, 17), data[0]);
        Assert.Equal(DateTimeKind.Utc, data[0].Kind);
    }

    #endregion


    #region WriteAsyncTests

    // ─── WriteAsync ─────────────────────────────────────────────────────────
    // Roundtrip tests: write events to a MemoryStream, then read back via
    // ParquetReader and verify schema + row count + sample values. This exercises
    // the full BuildSchema + BuildColumns + Parquet.NET write pipeline end-to-end.

    // Empty input still produces a structurally-valid Parquet file (header + footer
    // + schema, zero rows). Downstream analytics tools must be able to open it
    // without error even when a partition had no events.
    [Fact]
    public async Task WriteAsync_EmptyEvents_ProducesReadableParquetWithZeroRows()
    {
        // Arrange
        var writer = new ParquetArchiveWriter();
        using var stream = new MemoryStream();

        // Act
        await writer.WriteAsync(stream, Array.Empty<ArchiveEvent>(), CancellationToken.None);

        // Assert — file readable, schema preserved, zero rows
        stream.Position = 0;
        using var reader = await Parquet.ParquetReader.CreateAsync(stream);
        Assert.Equal(24, reader.Schema.Fields.Count);
        Assert.Equal(0, reader.RowGroupCount > 0
            ? reader.OpenRowGroupReader(0).RowCount
            : 0);
    }

    // Roundtrip for populated input: write a vehicle + alert pair, read back,
    // verify row count and a sample of column values per event type.
    [Fact]
    public async Task WriteAsync_MixedEvents_RoundTripsValues()
    {
        // Arrange
        var vehicleEvent = MakeVehicleEvent(eventId: "vu-1", vehicleId: "VH-001");
        var alertEvent = MakeAlertEvent(eventId: "sa-1", alertId: "ALERT-1");
        var events = new[] { vehicleEvent, alertEvent };
        var writer = new ParquetArchiveWriter();
        using var stream = new MemoryStream();

        // Act
        await writer.WriteAsync(stream, events, CancellationToken.None);

        // Assert — round-trip read
        stream.Position = 0;
        using var reader = await Parquet.ParquetReader.CreateAsync(stream);
        Assert.Equal(1, reader.RowGroupCount);

        using var rowGroup = reader.OpenRowGroupReader(0);
        Assert.Equal(2, rowGroup.RowCount);

        // eventId — required string column
        var eventIdField = reader.Schema.DataFields.First(f => f.Name == "eventId");
        var eventIdColumn = await rowGroup.ReadColumnAsync(eventIdField);
        Assert.Equal(new[] { "vu-1", "sa-1" }, (string[])eventIdColumn.Data);

        // vehicleId — nullable string column (value, then null)
        var vehicleIdField = reader.Schema.DataFields.First(f => f.Name == "vehicleId");
        var vehicleIdColumn = await rowGroup.ReadColumnAsync(vehicleIdField);
        var vehicleIdData = (string?[])vehicleIdColumn.Data;
        Assert.Equal("VH-001", vehicleIdData[0]);
        Assert.Null(vehicleIdData[1]);

        // alertId — nullable string column (null, then value)
        var alertIdField = reader.Schema.DataFields.First(f => f.Name == "alertId");
        var alertIdColumn = await rowGroup.ReadColumnAsync(alertIdField);
        var alertIdData = (string?[])alertIdColumn.Data;
        Assert.Null(alertIdData[0]);
        Assert.Equal("ALERT-1", alertIdData[1]);
    }

    #endregion
}
