namespace report.Models;

public class ReportRequest
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? ProsthesisType { get; set; }
    public string? MuscleGroup { get; set; }
    public string? Format { get; set; } = "json"; // json, csv, summary
}
