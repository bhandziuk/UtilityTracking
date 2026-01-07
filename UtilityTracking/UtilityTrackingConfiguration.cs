using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UtilityTracking;
using UtilityTracking.GeorgiaPower;

public static class UtilityTrackingConfiguration
{
    public static void ConfigureUtilityTracking(this IServiceCollection services, IConfigurationRoot config)
    {
        services.ConfigureRequiredSettings(config, typeof(GeorgiaPowerCredentials));
        services.AddSingleton(provider =>
        {
            var handler = new HttpClientHandler()
            {
                AllowAutoRedirect = false
            };

            return new HttpClient(handler);
        });
        services.AddTransient<Requests>();
    }
}
