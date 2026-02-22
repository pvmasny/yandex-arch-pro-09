namespace BionicProAuth.Models
{
    public class BionicProSessionOptions
    {
        public const string Position = "Session";

        public const string CookieName = "bionic_pro_session_id";

        public int TimeoutMinutes { get; set; } = 30;

        public bool SlidingExpiration { get; set; } = true;

        public string Key { get; set; }
    }
}
