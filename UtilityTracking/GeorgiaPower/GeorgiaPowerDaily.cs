using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public record GeorgiaPowerDaily(DateTime Date, double Cost, double LowTemp, double HighTemp, double Usage);
}
