using report.Services;
using System.Net;
namespace report.Middlewares;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenAuthMiddleware> _logger;

    public TokenAuthMiddleware(RequestDelegate next, ILogger<TokenAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AuthServiceClient authService)
    {
        // Пропускаем health check и swagger
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.Contains("/health") || path.Contains("/swagger") || path.Contains("/favicon") || path.Contains("/api/reports/user2"))
        {
            await _next(context);
            return;
        }
        var sessionId = context.Request.Cookies["bionic_pro_session_id"];
        // Валидируем токен через AuthService
        var validationResult = await authService.ValidateSesseionAsync(sessionId);

        if (!validationResult.HasAccess)
        {
            _logger.LogWarning("Invalid token for path: {Path}", path);
            context.Response.StatusCode = validationResult.StatusCode == 401 ? 401 : 403;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Invalid token",
                detail = validationResult.Error
            });
            return;
        }

        // Сохраняем информацию о пользователе в контексте для использования в эндпоинтах
        context.Items["UserId"] = validationResult.UserId;
        context.Items["CrmId"] = validationResult.CrmId;
        context.Items["Username"] = validationResult.Username;
        context.Items["Roles"] = validationResult.Roles;
        context.Items["Token"] = validationResult.Token;
        context.Items["TokenExpiresAt"] = validationResult.ExpiresAt;

        await _next(context);
    }
}
