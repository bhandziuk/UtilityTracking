using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Text.Json.Serialization;
using UtilityTracking;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UtilityTracking.UtilityDatabase;

namespace UtilityTracking;

public class Program()
{
    public static async Task Main(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appSettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<Program>();

        var config = builder.Build();

        var services = new ServiceCollection();

        services.ConfigureUtilityTracking(config);

        // When used as a console app this is required. If this is used as a library then FetchRange is not required.
        services.ConfigureRequiredSettings(config, typeof(FetchRange));

        var provider = services.BuildServiceProvider();

        PerformFetch(provider);
    }

    public async static void PerformFetch(IServiceProvider provider)
    {
        var gaPower = provider.GetRequiredService<GeorgiaPower.Requests>();
        var credentials = provider.GetService<GeorgiaPower.GeorgiaPowerCredentials>();
        if (credentials != null)
        {
            await gaPower.Authenticate(credentials);
        }
        else
        {
            // prompt for credentials then authenticate
        }
        // When used as a console app this is required. If this is used as a library then FetchRange is not required.
        var fetchRange = provider.GetRequiredService<FetchRange>();

        var hourlyData = await gaPower.Hourly(fetchRange.StartDate, fetchRange.EndDate);
        UtilityDatabase.GeorgiaPowerDatabase.WriteHourlyDataToSqlite(gaPower.Account!.AccountNumber, hourlyData);

        var dailyData = await gaPower.Daily(fetchRange.StartDate, fetchRange.EndDate);
        UtilityDatabase.GeorgiaPowerDatabase.WriteDailyDataToSqlite(gaPower.Account!.AccountNumber, dailyData);
    }
}