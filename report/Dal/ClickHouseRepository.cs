using ClickHouse.Client.ADO;
using report.Dal.Models;
using System.Text;

namespace report.Dal;

public class ClickHouseRepository : IClickHouseRepository
{
    private readonly ILogger<ClickHouseRepository> _logger;
    private readonly string _connectionString;
    public const string TableName = "bionicpro_reports";

    public ClickHouseRepository(IConfiguration configuration, ILogger<ClickHouseRepository> logger)
    {
        _logger = logger;
        var chConfig = configuration.GetSection("ConnectionStrings");

        _connectionString = chConfig["ClickHouse"]!;
    }

    public async Task<List<ProsthesisReport>> GetUserReportsAsync(
        string userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? prosthesisType = null,
        string? muscleGroup = null,
        CancellationToken cancellationToken = default)
    {
        var reports = new List<ProsthesisReport>();

        try
        {
            using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var queryBuilder = new StringBuilder($"""
                SELECT 
                    user_id,
                    date,
                    crm_name,
                    crm_age,
                    crm_gender,
                    prosthesis_type,
                    muscle_group,
                    signals_count,
                    signal_frequency_avg,
                    signal_duration_avg,
                    signal_amplitude_avg,
                    signal_duration_total
                FROM {TableName}
                WHERE user_id = '{userId}'
                """);

            if (startDate.HasValue)
                queryBuilder.Append($" AND date >= '{startDate.Value:yyyy-MM-dd}'");

            if (endDate.HasValue)
                queryBuilder.Append($" AND date <= '{endDate.Value:yyyy-MM-dd}'");

            if (!string.IsNullOrEmpty(prosthesisType))
                queryBuilder.Append($" AND prosthesis_type = '{prosthesisType}'");

            if (!string.IsNullOrEmpty(muscleGroup))
                queryBuilder.Append($" AND muscle_group = '{muscleGroup}'");

            queryBuilder.Append(" ORDER BY date DESC");

            using var command = connection.CreateCommand();
            command.CommandText = queryBuilder.ToString();
            command.CommandTimeout = 30;

            _logger.LogInformation("Executing query: {Query}", command.CommandText);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var report = new ProsthesisReport
                {
                    UserId = reader.GetString(0),
                    Date = reader.GetString(1),
                    CrmName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CrmAge = reader.IsDBNull(3) ? null : Convert.ToByte(reader.GetValue(3)),
                    CrmGender = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ProsthesisType = reader.GetString(5),
                    MuscleGroup = reader.GetString(6),

                    SignalsCount = reader.IsDBNull(7) ? 0u : Convert.ToUInt32(reader.GetValue(7)),
                    SignalFrequencyAvg = reader.IsDBNull(8) ? 0f : Convert.ToSingle(reader.GetValue(8)),
                    SignalDurationAvg = reader.IsDBNull(9) ? 0f : Convert.ToSingle(reader.GetValue(9)),
                    SignalAmplitudeAvg = reader.IsDBNull(10) ? 0f : Convert.ToSingle(reader.GetValue(10)),
                    SignalDurationTotal = reader.IsDBNull(11) ? 0u : Convert.ToUInt32(reader.GetValue(11))
                };
                reports.Add(report);
             }

            _logger.LogInformation("Found {Count} reports for user {UserId}", reports.Count, userId);
            return reports;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching reports for user {UserId}", userId);
            throw;
        }
    }

    public async Task<Dictionary<string, object>> GetUserSummaryAsync(
    string userId,
    CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var query = $"""
            SELECT 
                COUNT(*) as total_sessions,
                SUM(signals_count) as total_signals,
                AVG(signal_frequency_avg) as overall_avg_frequency,
                AVG(signal_amplitude_avg) as overall_avg_amplitude,
                SUM(signal_duration_total) as lifetime_duration,
                COUNT(DISTINCT prosthesis_type) as prosthesis_types_used,
                MIN(date) as first_report_date,
                MAX(date) as last_report_date,
                COUNT(DISTINCT left(date, 7)) as active_months,
                AVG(signal_duration_avg) as avg_session_duration,
                MIN(signal_frequency_avg) as min_frequency,
                MAX(signal_frequency_avg) as max_frequency,
                SUM(CASE WHEN signals_count > 1000 THEN 1 ELSE 0 END) as high_activity_days
            FROM {TableName}
            WHERE user_id = '{userId}'
            """;

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var summary = new Dictionary<string, object>();

            if (await reader.ReadAsync(cancellationToken))
            {
                // Helper function для безопасного чтения
                T SafeGet<T>(int index, T defaultValue = default)
                {
                    if (reader.IsDBNull(index))
                        return defaultValue;

                    try
                    {
                        var value = reader.GetValue(index);
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }

                // Основные метрики
                summary["total_sessions"] = SafeGet<ulong>(0, 0);
                summary["total_signals"] = SafeGet<ulong>(1, 0);
                summary["overall_avg_frequency"] = Math.Round(SafeGet<double>(2, 0), 2);
                summary["overall_avg_amplitude"] = Math.Round(SafeGet<double>(3, 0), 2);
                summary["lifetime_duration_seconds"] = SafeGet<ulong>(4, 0);
                summary["prosthesis_types_used"] = SafeGet<ulong>(5, 0);
                summary["first_report_date"] = SafeGet<string>(6, "N/A");
                summary["last_report_date"] = SafeGet<string>(7, "N/A");
                summary["active_months"] = SafeGet<ulong>(8, 0);

                // Дополнительные метрики
                summary["avg_session_duration"] = Math.Round(SafeGet<double>(9, 0), 2);
                summary["min_frequency"] = Math.Round(SafeGet<double>(10, 0), 2);
                summary["max_frequency"] = Math.Round(SafeGet<double>(11, 0), 2);
                summary["high_activity_days"] = SafeGet<ulong>(12, 0);

                // Вычисляемые метрики
                var totalSignals = SafeGet<ulong>(1, 0);
                var totalSessions = SafeGet<ulong>(0, 0);
                var totalDuration = SafeGet<ulong>(4, 0);
                var activeMonths = SafeGet<ulong>(8, 1);

                if (totalSessions > 0)
                {
                    summary["avg_signals_per_session"] = Math.Round((double)totalSignals / totalSessions, 2);
                    summary["avg_duration_per_session"] = Math.Round((double)totalDuration / totalSessions, 2);
                }

                if (activeMonths > 0)
                {
                    summary["avg_signals_per_month"] = Math.Round((double)totalSignals / activeMonths, 2);
                    summary["avg_sessions_per_month"] = Math.Round((double)totalSessions / activeMonths, 2);
                }

                // Процент высокой активности
                var highActivityDays = SafeGet<ulong>(12, 0);
                if (totalSessions > 0)
                {
                    summary["high_activity_percentage"] = Math.Round(
                        (double)highActivityDays / totalSessions * 100, 2);
                }

                // Добавляем метаданные
                summary["generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                summary["user_id"] = userId;

                _logger.LogInformation("Generated summary for user {UserId} with {Count} metrics",
                    userId, summary.Count);
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary for user {UserId}", userId);
            throw;
        }
    }


    public async Task<bool> TableExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var query = $"""
                SELECT COUNT(*) 
                FROM system.tables 
                WHERE database = 'default' AND name = '{TableName}'
                """;

            using var command = connection.CreateCommand();
            command.CommandText = query;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if table exists");
            return false;
        }
    }

    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        if (await TableExistsAsync(cancellationToken))
            return;

        try
        {
            using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var createTableQuery = $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    user_id String,
                    date Date,
                    crm_name String,
                    crm_age Int32,
                    crm_gender String,
                    prosthesis_type String,
                    muscle_group String,
                    signals_count Int32,
                    signal_frequency_avg Float64,
                    signal_duration_avg Float64,
                    signal_amplitude_avg Float64,
                    signal_duration_total Int32
                ) ENGINE = MergeTree()
                ORDER BY (date, user_id)
                """;

            using var command = connection.CreateCommand();
            command.CommandText = createTableQuery;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Table {TableName} created successfully", TableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating table {TableName}", TableName);
            throw;
        }
    }
}
