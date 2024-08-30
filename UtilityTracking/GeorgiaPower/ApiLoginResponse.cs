using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public class ApiLoginResponse(double statusCode, string message, bool isSuccess, object modelErrors)
    {
        public double StatusCode { get; set; } = statusCode;
        public string Message { get; set; } = message;
        public bool IsSuccess { get; set; } = isSuccess;
        public object ModelErrors { get; set; } = modelErrors;
    }
}
