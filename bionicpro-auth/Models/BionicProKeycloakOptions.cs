namespace BionicProAuth.Models
{
    public class BionicProKeycloakOptions
    {
        public const string Position = "Keycloak";

        public string BaseUrl { get; set; }

        public string Realm { get; set; }

        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        public int AccessTokenLifetime { get; set; } = 120;

    }
}
