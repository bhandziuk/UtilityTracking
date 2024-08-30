using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public record Account(string AccountNumber, MeterServicePoint MeterServicePoint, string Company);
}
