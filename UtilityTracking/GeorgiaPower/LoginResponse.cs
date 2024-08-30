using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UtilityTracking.GeorgiaPower
{
    public class LoginResponseData(double result, string token, string errorMessage, IEnumerable<string> messages, string username, object rememberUsername, object staySignedIn, object recaptchaResponse, double targetPage, object @params, string html, object redirect, object origin)
    {
        public double Result { get; set; } = result;
        public string Token { get; set; } = token;
        public string ErrorMessage { get; set; } = errorMessage;
        public IEnumerable<string> Messages { get; set; } = messages;
        public string Username { get; set; } = username;
        public object RememberUsername { get; set; } = rememberUsername;
        public object StaySignedIn { get; set; } = staySignedIn;
        public object RecaptchaResponse { get; set; } = recaptchaResponse;
        public double TargetPage { get; set; } = targetPage;
        public object @Params { get; set; } = @params;
        public string Html { get; set; } = html;
        public object Redirect { get; set; } = redirect;
        public object Origin { get; set; } = origin;
    }

    public class LoginResponse(LoginResponseData data, double statusCode, string message, bool isSuccess, object modelErrors) : ApiLoginResponse(statusCode, message, isSuccess, modelErrors)
    {
        public LoginResponseData Data { get; set; } = data;
    }
}
