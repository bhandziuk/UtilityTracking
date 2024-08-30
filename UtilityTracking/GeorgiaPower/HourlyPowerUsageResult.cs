using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public record HourlyPowerUsageResult(XAxis XAxis, AllHourlySeries Series);

    public record AllHourlySeries(Series Cost, Series Usage, Series CostDelayed, Series UsageDelayed, Series Temp, Series SolarGeneration, Series SolarGenerationDelayed);
    public record AllDailySeries(Series WeekdayCost, Series WeekendCost, Series WeekdayUsage, Series WeekendUsage, Series CostDelayed, Series UsageDelayed, Series Overage, Series HighTemp, Series LowTemp, Series SolarGeneration, Series SolarGenerationDelayed, Series AvgDailyCost, Series AlertCost);

    public record Series(IEnumerable<DataPoint> Data);

    public record DataPoint(double X, double Y, DateTime Name, string Resolution);

    public record XAxis(IEnumerable<DateTime> Labels);


    public record DailyPowerUsageResult(XAxis XAxis, AllDailySeries Series, string DailyDataSource);

}
