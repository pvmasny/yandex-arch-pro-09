namespace BionicProAuth.Models
{
    public class SessionData
    {
        public required string SessionId { get; set; }
        public required string AccessToken { get; set; }
        public required string RefreshToken { get; set; }
        public required string UserId { get; set; }
        public required string Username { get; set; }
        public DateTime AccessTokenExpiresAt { get; set; }
        public DateTime RefreshTokenExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
    }
}
