// HivePartitionPathTests.cs
// -------------------------
// Unit tests for HivePartitionPath (SP1-15, ADR-0012).
// Pure-function tests — no DI, no mocks.
//
// Method under test: HivePartitionPath.ForHour(DateTimeOffset)
// Contract: returns the Hive-style partition folder path for the UTC hour
//           containing `timestamp`, zero-padded throughout.

using SydneyPulse.Core.Archive;
using Xunit;

namespace SydneyPulse.Tests.Unit.Archive;

public class HivePartitionPathTests
{
    // Happy path: a UTC DateTimeOffset produces the expected Hive partition path.
    // Establishes the basic format contract: "yyyy=YYYY/MM=MM/dd=DD/HH=HH".
    [Fact]
    public void ForHour_UtcInput_ReturnsHivePartitionPath()
    {
        // Arrange — explicit UTC offset
        var timestamp = new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero);

        // Act
        var result = HivePartitionPath.ForHour(timestamp);

        // Assert
        Assert.Equal("yyyy=2026/MM=06/dd=04/HH=14", result);
    }

    // Critical: a non-UTC input must be normalised to UTC BEFORE formatting.
    // 2026-06-04 14:23:17 Sydney time (+10:00) is 04:23 UTC — partition must be HH=04.
    // If this test fails the implementation is using LocalDateTime, not UtcDateTime.
    [Fact]
    public void ForHour_NonUtcOffset_NormalisesToUtcBeforeFormatting()
    {
        // Arrange — Sydney standard time
        var timestamp = new DateTimeOffset(2026, 6, 4, 14, 23, 17, TimeSpan.FromHours(10));

        // Act
        var result = HivePartitionPath.ForHour(timestamp);

        // Assert — note HH=04 (UTC), NOT HH=14 (Sydney local)
        Assert.Equal("yyyy=2026/MM=06/dd=04/HH=04", result);
    }

    // Single-digit month / day / hour values must be zero-padded to two digits.
    // Partition pruning in analytics tools relies on fixed-width path components.
    [Fact]
    public void ForHour_SingleDigitValues_ArePaddedWithLeadingZeros()
    {
        // Arrange — month=1, day=5, hour=3 (all single-digit)
        var timestamp = new DateTimeOffset(2026, 1, 5, 3, 0, 0, TimeSpan.Zero);

        // Act
        var result = HivePartitionPath.ForHour(timestamp);

        // Assert — MM=01, dd=05, HH=03 (not MM=1, dd=5, HH=3)
        Assert.Equal("yyyy=2026/MM=01/dd=05/HH=03", result);
    }

    // Minutes and seconds within the hour are dropped — partition is by hour, not by rounded-hour.
    // 14:59:59 belongs to partition HH=14, not HH=15.
    [Fact]
    public void ForHour_EndOfHourInput_ReturnsContainingHour()
    {
        // Arrange — last second of the 14:00 hour
        var timestamp = new DateTimeOffset(2026, 6, 4, 14, 59, 59, TimeSpan.Zero);

        // Act
        var result = HivePartitionPath.ForHour(timestamp);

        // Assert — still HH=14, not HH=15
        Assert.Equal("yyyy=2026/MM=06/dd=04/HH=14", result);
    }

    // ─── ForFile ────────────────────────────────────────────────────────────
    // Method under test: HivePartitionPath.ForFile(DateTimeOffset, string)
    // Contract: returns the hour-partition path + "/" + fileName.
    //           UTC normalisation rules delegate to ForHour.

    // Happy path: a UTC DateTimeOffset + parquet filename produces a full Hive path.
    // Verifies the composition shape: "{ForHour(ts)}/{fileName}".
    [Fact]
    public void ForFile_UtcInputWithParquetFilename_ComposesFullPath()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero);
        var fileName = "events-20260604T143012.parquet";

        // Act
        var result = HivePartitionPath.ForFile(timestamp, fileName);

        // Assert
        Assert.Equal("yyyy=2026/MM=06/dd=04/HH=14/events-20260604T143012.parquet", result);
    }

    // Critical: UTC normalisation must apply to ForFile too — non-UTC offsets are
    // converted before formatting. If this fails, ForFile is bypassing ForHour.
    [Fact]
    public void ForFile_NonUtcOffset_NormalisesToUtcInPath()
    {
        // Arrange — Sydney standard time (+10:00) at 14:23 = UTC 04:23
        var timestamp = new DateTimeOffset(2026, 6, 4, 14, 23, 17, TimeSpan.FromHours(10));
        var fileName = "events-test.parquet";

        // Act
        var result = HivePartitionPath.ForFile(timestamp, fileName);

        // Assert — HH=04 (UTC), NOT HH=14 (Sydney local)
        Assert.Equal("yyyy=2026/MM=06/dd=04/HH=04/events-test.parquet", result);
    }

    // Filename is appended verbatim regardless of shape — manifest, parquet,
    // or anything else with dots/underscores/hyphens passes through unchanged.
    // (Filename validation is the caller's responsibility, not this helper's.)
    [Fact]
    public void ForFile_ManifestFilename_AppendsVerbatim()
    {
        // Arrange — _manifest.json has leading underscore + dot, common edge shape
        var timestamp = new DateTimeOffset(2026, 6, 4, 14, 0, 0, TimeSpan.Zero);
        var fileName = "_manifest.json";

        // Act
        var result = HivePartitionPath.ForFile(timestamp, fileName);

        // Assert — special chars preserved exactly
        Assert.Equal("yyyy=2026/MM=06/dd=04/HH=14/_manifest.json", result);
    }

    // ─── Parse ──────────────────────────────────────────────────────────────
    // Method under test: HivePartitionPath.Parse(string)
    // Contract: returns the DateTimeOffset marking the START of the hour
    //           encoded in the partition path. UTC offset always.

    // Happy path: a well-formed Hive partition path parses back to the exact
    // start of that hour in UTC. Establishes the basic format contract.
    [Fact]
    public void Parse_WellFormedHivePath_ReturnsStartOfHourInUtc()
    {
        // Arrange
        var partitionPath = "yyyy=2026/MM=06/dd=05/HH=09";

        // Act
        var result = HivePartitionPath.Parse(partitionPath);

        // Assert — exact start of hour, UTC offset
        Assert.Equal(new DateTimeOffset(2026, 6, 5, 9, 0, 0, TimeSpan.Zero), result);
    }

    // Zero-padded single-digit components must parse correctly.
    // If int.Parse stumbles on "01" or "00" the implementation is wrong.
    [Fact]
    public void Parse_ZeroPaddedSingleDigits_ParsesCorrectly()
    {
        // Arrange — January 1st, midnight
        var partitionPath = "yyyy=2026/MM=01/dd=01/HH=00";

        // Act
        var result = HivePartitionPath.Parse(partitionPath);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), result);
    }

    // Round-trip contract: ForHour(ts).Parse() == start of ts's hour, in UTC.
    // Pins the inverse relationship explicitly so future edits to either method
    // can't drift apart silently.
    [Fact]
    public void Parse_RoundTripFromForHour_RecoversStartOfHour()
    {
        // Arrange — non-UTC source so we also verify the UTC normalisation chain
        var original = new DateTimeOffset(2026, 6, 4, 14, 23, 17, TimeSpan.FromHours(10));
        var partitionPath = HivePartitionPath.ForHour(original);

        // Act
        var result = HivePartitionPath.Parse(partitionPath);

        // Assert — UTC start of hour (04:00, not 14:00 — original was Sydney time)
        Assert.Equal(new DateTimeOffset(2026, 6, 4, 4, 0, 0, TimeSpan.Zero), result);
    }

    // Defensive: malformed input means upstream listing got something it shouldn't.
    // Throw loudly so the flush tick fails fast rather than silently skipping
    // a partition (which would mean events linger in pending forever).
    [Fact]
    public void Parse_MalformedInput_Throws()
    {
        // Arrange — wrong segment count
        var partitionPath = "yyyy=2026/MM=06/dd=05";  // missing HH

        // Act + Assert — exact exception type left to implementer; just require throw
        Assert.ThrowsAny<Exception>(() => HivePartitionPath.Parse(partitionPath));
    }
}
