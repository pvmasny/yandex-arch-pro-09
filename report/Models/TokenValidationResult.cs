namespace report.Models;

public class TokenValidationResult
{
    public bool HasAccess { get; set; }
    public string? UserId { get; set; }
    public int CrmId { get; set; }
    public string? Username { get; set; }
    public string Token { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime? ExpiresAt { get; set; }
    public string? Error { get; set; }
    public int StatusCode { get; set; }
    public bool IsAdmin => Roles.Contains("admin") || Roles.Contains("reports-admin");
}

public class AccessValidationResult
{
    public bool HasAccess { get; set; }
    public int CrmId { get; set; }
    public string? CurrentUserId { get; set; }
    public string? TargetUserId { get; set; }
    public bool IsAdmin { get; set; }
    public string? Error { get; set; }
}

public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool IsActive { get; set; }
}
