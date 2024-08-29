using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public record PowerUsageResult(XAxis XAxis, AllSeries Series);

    public record AllSeries(Series Cost, Series Usage, Series CostDelayed, Series UsageDelayed, Series Temp, Series SolarGeneration, Series SolarGenerationDelayed);

    public record Series(IEnumerable<DataPoint> Data);

    public record DataPoint(double X, double Y, DateTime Name, string Resolution);

    public record XAxis(IEnumerable<DateTime> Labels);
}
