using report.Services;
using System.ComponentModel.DataAnnotations;

namespace report.Middlewares;

public class UserAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserAccessMiddleware> _logger;

    public UserAccessMiddleware(RequestDelegate next, ILogger<UserAccessMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AuthServiceClient authService)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // Извлекаем userId из пути если есть
        var targetUserId = ExtractUserIdFromPath(path);

        if (!string.IsNullOrEmpty(targetUserId))
        {
            var token = context.Items["Token"]?.ToString();
            var currentUserId = context.Items["UserId"]?.ToString();
            var roles = context.Items["Roles"] as List<string> ?? new();

            if (string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "No token in context" });
                return;
            }

            // Проверяем доступ через AuthService
            var accessResult = await authService.ValidateUserAccessAsync(token, targetUserId);

            if (!accessResult.HasAccess)
            {
                _logger.LogWarning("Access denied: User {CurrentUserId} -> Target {TargetUserId}",
                    currentUserId, targetUserId);

                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Access denied",
                    message = "You can only access your own data",
                    currentUserId = currentUserId,
                    targetUserId = targetUserId
                });
                return;
            }

            _logger.LogInformation("Access granted: User {CurrentUserId} -> Target {TargetUserId}",
                currentUserId, targetUserId);

            // Добавляем информацию о доступе в контекст
            context.Items["AccessGranted"] = true;
            context.Items["TargetUserId"] = targetUserId;
            context.Items["IsAdmin"] = accessResult.IsAdmin;
            context.Items["CrmId"] = accessResult.CrmId;
        }

        await _next(context);
    }

    private string? ExtractUserIdFromPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i] == "user" && i + 1 < segments.Length)
            {
                var nextSegment = segments[i + 1];
                // Проверяем, что это действительно userId, а не ключевое слово
                if (nextSegment != "summary" &&
                    nextSegment != "months" &&
                    nextSegment != "month" &&
                    nextSegment != "all" &&
                    !nextSegment.Contains('?'))
                {
                    return nextSegment;
                }
            }
        }

        return null;
    }
}
