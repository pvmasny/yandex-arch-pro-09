namespace report.Infrastructure;

public interface IMinioService
{
    Task<bool> IsExistsAsync(string userId, string reportKey);
    Task<string> GetReportUrlAsync(string userId, string reportKey);
    Task SaveReportAsync(string userId, string reportKey, object reportData, string format = "json");
    Task<string> GeneratePresignedUrlAsync(string userId, string reportKey, int expiryInMinutes = 60);
}
