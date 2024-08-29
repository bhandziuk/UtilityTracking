using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public record GeorgiaPowerHourly(DateTime Date, double Cost, double Temp, double Usage);
}
