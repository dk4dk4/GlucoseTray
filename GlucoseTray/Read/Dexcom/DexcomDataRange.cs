using System.Text.Json.Serialization;

namespace GlucoseTray.Read.Dexcom;

public class DexcomDataRange
{
    [JsonPropertyName("egvRangeIncludesLastEgvTime")]
    public long LatestEgvTimeMs { get; set; }

    [JsonPropertyName("egvRangeIncludesNextEgvTime")]
    public long? NextEgvTimeMs { get; set; }

    [JsonPropertyName("chartRangeStartTime")]
    public long ChartRangeStartMs { get; set; }

    [JsonPropertyName("chartRangeEndTime")]
    public long ChartRangeEndMs { get; set; }
}
