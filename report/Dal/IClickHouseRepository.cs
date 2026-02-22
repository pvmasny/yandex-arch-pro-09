using report.Dal.Models;

namespace report.Dal;

public interface IClickHouseRepository
{
    Task<List<ProsthesisReport>> GetUserReportsAsync(
        string userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? prosthesisType = null,
        string? muscleGroup = null,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, object>> GetUserSummaryAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<bool> TableExistsAsync(CancellationToken cancellationToken = default);
    Task EnsureTableExistsAsync(CancellationToken cancellationToken = default);
}
