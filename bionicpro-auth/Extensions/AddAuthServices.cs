using BionicProAuth.Models;
using BionicProAuth.Services;
namespace BionicProAuth.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BionicProKeycloakOptions>(configuration.GetSection(BionicProKeycloakOptions.Position));
        services.Configure<BionicProSessionOptions>(configuration.GetSection(BionicProSessionOptions.Position));

        var keycloakOptions = configuration
            .GetSection(BionicProKeycloakOptions.Position)
            .Get<BionicProKeycloakOptions>();
        
        // Регистрация HTTP клиентов
        services.AddHttpClient<KeycloakService>(client =>
        {
            client.BaseAddress = new Uri(keycloakOptions!.BaseUrl!);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddScoped<SessionService>();
        services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();

        if (!string.IsNullOrEmpty(configuration["Redis:ConnectionString"]))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = configuration["Redis:ConnectionString"];
                options.InstanceName = "BionicProAuth";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}