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

var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>();

var config = builder.Build();


var services = new ServiceCollection();

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

var provider = services.BuildServiceProvider();


var gaPower = provider.GetRequiredService<Requests>();
var credentials = provider.GetService<GeorgiaPowerCredentials>();

await gaPower.Authenticate(credentials);

var startDate = DateTime.Parse("08/26/2023");
var endDate = DateTime.Parse("08/27/2024");

var hourlyData = await gaPower.Hourly(startDate, endDate);

var rootDir = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.Parent.FullName;

var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDomain.CurrentDomain.FriendlyName);

//var hourlyText = File.OpenText(Path.Combine(rootDir, "Hourly GA Power Data.json")).ReadToEnd();

//var serializer = new JsonSerializerSettings
//{
//    ContractResolver = new CamelCasePropertyNamesContractResolver(),
//    Converters = { new IsoDateTimeConverter() }
//};

//var data = JsonConvert.DeserializeObject<PowerUsageResult>(hourlyText, serializer);

//var total = data.Series.Cost.Data.Count();

Directory.CreateDirectory(appData);

var sqliteFile = Path.Combine(appData, gaPower.Account.AccountNumber + ".db");

using var connection = new SqliteConnection($"Data Source={sqliteFile};");

connection.Open();

using var createGaPowerHourly = connection.CreateCommand();
createGaPowerHourly.CommandText = "CREATE TABLE if not exists GeorgiaPowerHourly (Date DATETIME PRIMARY KEY, Cost DOUBLE,Usage DOUBLE,[Temp] DOUBLE);";
createGaPowerHourly.ExecuteNonQuery();

var costs = hourlyData.Series.Cost.Data.ToDictionary(o => o.Name, o => o.Y);
var usages = hourlyData.Series.Usage.Data.ToDictionary(o => o.Name, o => o.Y);
var temps = hourlyData.Series.Temp.Data.ToDictionary(o => o.Name, o => o.Y);

var allData = costs.Select(o => new GeorgiaPowerHourly(o.Key, o.Value, temps[o.Key], usages[o.Key]));

var transaction = connection.BeginTransaction();

foreach (var point in allData)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "Insert into GeorgiaPowerHourly(Date, Cost, Usage, [Temp]) values(@Date, @Cost, @Usage, @Temp)";
    cmd.Parameters.Add(new SqliteParameter("@Date", point.Date));
    cmd.Parameters.Add(new SqliteParameter("@Cost", point.Cost));
    cmd.Parameters.Add(new SqliteParameter("@Usage", point.Usage));
    cmd.Parameters.Add(new SqliteParameter("@Temp", point.Temp));
    cmd.ExecuteNonQuery();
}

transaction.Commit();
