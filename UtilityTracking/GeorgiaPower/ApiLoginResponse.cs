using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public class ApiLoginResponse
    {
        public double StatusCode { get; set; }
        public string Message { get; set; }
        public bool IsSuccess { get; set; }
        public object ModelErrors { get; set; }
    }
}
