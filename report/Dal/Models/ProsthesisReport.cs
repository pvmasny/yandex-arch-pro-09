using System.Text.Json.Serialization;

namespace report.Dal.Models;

public class ProsthesisReport
{
    public string UserId { get; set; } = string.Empty;
    public string Date { get; set; }
    public string? CrmName { get; set; }
    public int? CrmAge { get; set; }
    public string? CrmGender { get; set; }
    public string ProsthesisType { get; set; } = string.Empty;
    public string MuscleGroup { get; set; } = string.Empty;
    public uint SignalsCount { get; set; }
    public float SignalFrequencyAvg { get; set; }
    public float SignalDurationAvg { get; set; }
    public float SignalAmplitudeAvg { get; set; }
    public uint SignalDurationTotal { get; set; }

    [JsonIgnore]
    public int SignalsCountAsInt => (int)SignalsCount;

    [JsonIgnore]
    public int TotalDurationAsInt => (int)SignalDurationTotal;

    public double AvgQualityScore => (SignalsCountAsInt * 0.3 + SignalAmplitudeAvg * 0.3 + (SignalDurationAvg / 100) * 0.4);
    public string ActivityLevel => SignalsCount switch
    {
        > 1000 => "High",
        > 500 => "Medium",
        > 100 => "Low",
        _ => "Minimal"
    };
}