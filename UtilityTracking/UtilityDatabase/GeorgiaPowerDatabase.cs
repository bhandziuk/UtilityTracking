using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UtilityTracking.GeorgiaPower;

namespace UtilityTracking.UtilityDatabase
{
    public class GeorgiaPowerDatabase
    {
        public static string GetSqlitePath(string accountNumber)
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDomain.CurrentDomain.FriendlyName);

            Directory.CreateDirectory(appData);
            // var date = DateTime.Now.ToString("yyyyMMdd");
            // return Path.Combine(appData, $"{accountNumber}-{date}.db");
            return Path.Combine(appData, $"{accountNumber}.db");
        }

        public static void WriteDailyDataToSqlite(DbConnection openConnection, DailyPowerUsageResult dailyData)
        {
            using var createGaPowerHourly = openConnection.CreateCommand();
            createGaPowerHourly.CommandText = "CREATE TABLE if not exists GeorgiaPowerDaily (Date DATETIME PRIMARY KEY, Cost DOUBLE,Usage DOUBLE, LowTemp DOUBLE, HighTemp DOUBLE);";
            createGaPowerHourly.ExecuteNonQuery();

            var costs = dailyData.Series.WeekdayCost.Data.Concat(dailyData.Series.WeekendCost.Data).ToDictionary(o => o.Name, o => o.Y);
            var usages = dailyData.Series.WeekdayUsage.Data.Concat(dailyData.Series.WeekendUsage.Data).ToDictionary(o => o.Name, o => o.Y);
            var highTemps = dailyData.Series.HighTemp.Data.ToDictionary(o => o.Name, o => o.Y);
            var lowTemps = dailyData.Series.LowTemp.Data.ToDictionary(o => o.Name, o => o.Y);

            var allData = costs.Select(o => new GeorgiaPowerDaily(o.Key, o.Value, lowTemps[o.Key], highTemps[o.Key], usages[o.Key]));

            var transaction = openConnection.BeginTransaction();

            foreach (var point in allData)
            {
                using var cmd = openConnection.CreateCommand();
                cmd.CommandText = "Insert or Replace into GeorgiaPowerDaily(Date, Cost, Usage, LowTemp, HighTemp) Select @Date, @Cost, @Usage, @LowTemp, @HighTemp";
                cmd.Parameters.Add(new SqliteParameter("@Date", point.Date));
                cmd.Parameters.Add(new SqliteParameter("@Cost", point.Cost));
                cmd.Parameters.Add(new SqliteParameter("@Usage", point.Usage));
                cmd.Parameters.Add(new SqliteParameter("@LowTemp", point.LowTemp));
                cmd.Parameters.Add(new SqliteParameter("@HighTemp", point.HighTemp));
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        public static void WriteDailyDataToSqlite(string accountNumber, DailyPowerUsageResult dailyData)
        {
            var sqliteFile = GetSqlitePath(accountNumber);
            Console.WriteLine("Opening sqlite for daily data: {0}", sqliteFile);
            using var connection = new SqliteConnection($"Data Source={sqliteFile};");

            connection.Open();
            WriteDailyDataToSqlite(connection, dailyData);
        }

        public static void WriteHourlyDataToSqlite(DbConnection openConnection, HourlyPowerUsageResult hourlyData)
        {
            using var createGaPowerHourly = openConnection.CreateCommand();
            createGaPowerHourly.CommandText = "CREATE TABLE if not exists GeorgiaPowerHourly (Date DATETIME PRIMARY KEY, Cost DOUBLE,Usage DOUBLE,[Temp] DOUBLE);";
            createGaPowerHourly.ExecuteNonQuery();

            var costs = hourlyData.Series.Cost.Data.ToDictionary(o => o.Name, o => o.Y);
            var usages = hourlyData.Series.Usage.Data.ToDictionary(o => o.Name, o => o.Y);
            var temps = hourlyData.Series.Temp.Data.ToDictionary(o => o.Name, o => o.Y);

            var allData = costs.Select(o => new GeorgiaPowerHourly(o.Key, o.Value, temps[o.Key], usages[o.Key]));

            var transaction = openConnection.BeginTransaction();

            foreach (var point in allData)
            {
                using var cmd = openConnection.CreateCommand();
                cmd.CommandText = "Insert or Replace into GeorgiaPowerHourly(Date, Cost, Usage, [Temp]) Select @Date, @Cost, @Usage, @Temp";
                cmd.Parameters.Add(new SqliteParameter("@Date", point.Date));
                cmd.Parameters.Add(new SqliteParameter("@Cost", point.Cost));
                cmd.Parameters.Add(new SqliteParameter("@Usage", point.Usage));
                cmd.Parameters.Add(new SqliteParameter("@Temp", point.Temp));
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public static void WriteHourlyDataToSqlite(string accountNumber, HourlyPowerUsageResult hourlyData)
        {
            var sqliteFile = GetSqlitePath(accountNumber);
            Console.WriteLine("Opening sqlite for hourly data: {0}", sqliteFile);
            using var connection = new SqliteConnection($"Data Source={sqliteFile};");

            connection.Open();
            WriteHourlyDataToSqlite(connection, hourlyData);
        }
    }
}
