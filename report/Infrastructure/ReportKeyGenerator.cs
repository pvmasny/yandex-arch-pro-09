
using report.Models;

public static class ReportKeyGenerator
{
    public static string GenerateKey(string userId, ReportRequest request, string format = "json")
    {
        var parts = new List<string> { "reports", userId };

        // Добавляем даты если есть
        if (request.StartDate.HasValue || request.EndDate.HasValue)
        {
            var start = request.StartDate?.ToString("yyyy-MM-dd") ?? "begin";
            var end = request.EndDate?.ToString("yyyy-MM-dd") ?? "end";
            parts.Add($"period_{start}_{end}");
        }

        // Добавляем фильтры
        if (!string.IsNullOrEmpty(request.ProsthesisType))
            parts.Add($"prosthesis_{request.ProsthesisType}");

        if (!string.IsNullOrEmpty(request.MuscleGroup))
            parts.Add($"muscle_{request.MuscleGroup}");

        // Добавляем формат
        parts.Add($"report.{format}");

        return string.Join("/", parts);
    }

    public static string GenerateHashKey(string userId, ReportRequest request, string format = "json")
    {
        // Создаем хеш от параметров запроса
        var parameters = new
        {
            userId,
            request.StartDate,
            request.EndDate,
            request.ProsthesisType,
            request.MuscleGroup,
            format
        };

        var json = System.Text.Json.JsonSerializer.Serialize(parameters);
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.Create()
            .ComputeHash(System.Text.Encoding.UTF8.GetBytes(json)))
            .Replace("/", "_")
            .Replace("+", "-");

        return $"reports/{userId}/hash/{hash}.{format}";
    }
}