using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UtilityTracking.GeorgiaPower
{
    public class LoginResponseData
    {
        public double Result { get; set; }
        public string Token { get; set; }
        public string ErrorMessage { get; set; }
        public IEnumerable<string> Messages { get; set; }
        public string Username { get; set; }
        public object RememberUsername { get; set; }
        public object StaySignedIn { get; set; }
        public object RecaptchaResponse { get; set; }
        public double TargetPage { get; set; }
        public object @Params { get; set; }
        public string Html { get; set; }
        public object Redirect { get; set; }
        public object Origin { get; set; }
    }

    public class LoginResponse : ApiLoginResponse
    {
        public LoginResponseData Data { get; set; }
    }
}
