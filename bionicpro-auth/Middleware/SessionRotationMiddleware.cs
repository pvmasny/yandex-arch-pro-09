using BionicProAuth.Models;
using BionicProAuth.Services;

namespace BionicProAuth.Middleware;

public class SessionRotationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionRotationMiddleware> _logger;

    public SessionRotationMiddleware(RequestDelegate next, ILogger<SessionRotationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SessionService sessionService)
    {
        var sessionId = context.Request.Cookies[BionicProSessionOptions.CookieName];
        bool needFinal = false;

        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await sessionService.GetSessionAsync(sessionId);

            if (session != null)
            {
                // Ротация сессии для предотвращения session fixation
                var newSessionId = await sessionService.PreRotateSessionAsync(sessionId);

                if (!string.IsNullOrEmpty(newSessionId))
                {
                    needFinal = true;
                    // Обновляем cookie с новым session_id
                    context.Response.Cookies.Append(BionicProSessionOptions.CookieName, newSessionId, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        MaxAge = TimeSpan.FromMinutes(30)
                    });

                    // Добавляем информацию о сессии в контекст для дальнейшего использования
                    context.Items["SessionId"] = newSessionId;
                    context.Items["SessionData"] = session;

                    _logger.LogDebug("Session rotated for request to {Path}", context.Request.Path);
                }
            }
        }

        await _next(context);
        if (needFinal)
        {
            sessionService.FinalRotateSessionAsync(sessionId);
        }
    }
}