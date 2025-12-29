using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Text.Json.Serialization;
using UtilityTracking.GeorgiaPower;
using UtilityTracking;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UtilityTracking.UtilityDatabase;

var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>();

var config = builder.Build();


var services = new ServiceCollection();

services.ConfigureRequiredSettings(config, typeof(GeorgiaPowerCredentials));
services.ConfigureRequiredSettings(config, typeof(FetchRange));
services.AddSingleton(provider =>
{
    var handler = new HttpClientHandler()
    {
        AllowAutoRedirect = false
    };

    return new HttpClient(handler);
});
services.AddTransient<Requests>();

var provider = services.BuildServiceProvider();


var gaPower = provider.GetRequiredService<Requests>();
var credentials = provider.GetService<GeorgiaPowerCredentials>();
if (credentials != null)
{
    await gaPower.Authenticate(credentials);
}
else
{
    // prompt for credentials then authenticate
}
var fetchRange = provider.GetRequiredService<FetchRange>();

var hourlyData = await gaPower.Hourly(fetchRange.StartDate, fetchRange.EndDate);
GeorgiaPower.WriteHourlyDataToSqlite(gaPower.Account!.AccountNumber, hourlyData);

var dailyData = await gaPower.Daily(fetchRange.StartDate, fetchRange.EndDate);
GeorgiaPower.WriteDailyDataToSqlite(gaPower.Account!.AccountNumber, dailyData);

