using BionicProAuth.Models;

namespace BionicProAuth.Extensions
{
    public static class KeycloakOptionsExtensions
    {
        /// <summary>
        /// Get realms url: $"{options.BaseUrl}/realms/{options.Realm}"
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        public static string GetRealmsUrl(this BionicProKeycloakOptions options)
        {
            return $"{options.BaseUrl}/realms/{options.Realm}";
        }

        public static string GetOpenidConnectTokenUrl(this BionicProKeycloakOptions options)
        {
            return $"{options.GetRealmsUrl()}/protocol/openid-connect/token";
        }

        public static string GetOpenidConnectTokenIntrospectUrl(this BionicProKeycloakOptions options)
        {
            return $"{options.GetOpenidConnectTokenUrl()}/introspect";
        }

        public static string GetOpenidConnectLogoutUrl(this BionicProKeycloakOptions options)
        {
            return $"{options.GetRealmsUrl()}/protocol/openid-connect/logout";
        }
    }
}
