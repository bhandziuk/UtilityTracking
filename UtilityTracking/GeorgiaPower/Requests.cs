using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Cache;
using System.Net.Http.Json;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;

namespace UtilityTracking.GeorgiaPower
{
    public class Requests(HttpClient client)
    {
        private readonly HttpClient Client = client;
        private string? Jwt;
        public Account? Account { get; private set; }

        public async Task Authenticate(UserCredentials credentials)
        {
            var verificationToken = await GetVerificationToken();
            var scWebToken = await GetScWebToken(credentials, verificationToken);
            Jwt = await GetJwt(scWebToken);

            var accountNumber = await GetAccountInfo();
            var meterServicePoint = await GetServicePointNumber(accountNumber);

            Account = new Account(accountNumber, meterServicePoint, "GPC");
        }

        public async Task<DailyPowerUsageResult> Daily(DateTime startDate, DateTime endDate)
        {
            if (Account == null)
            {
                throw new Exception("You need to authenticate first");
            }

            var builder = new UriBuilder($"https://customerservice2api.southerncompany.com/api/MyPowerUsage/MPUData/{Account.AccountNumber}/Daily");
            var query = HttpUtility.ParseQueryString(builder.Query);
            //query["params"] = "OPCO=GPC";
            query["StartDate"] = startDate.ToString("MM/dd/yyyy");
            query["EndDate"] = endDate.ToString("MM/dd/yyyy"); ;
            query["intervalBehavior"] = "Automatic";
            query["ServicePointNumber"] = Account.MeterServicePoint.ServicePointNumber;
            query["OPCO"] = Account.Company;

            builder.Query = query.ToString();
            string url = builder.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Bearer " + Jwt);

            var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Could not query MyPowerUsage/MPUData/AccountNumber/Daily");
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            if (data.TryGetValue("Data", out var dataOuterNode) && dataOuterNode.ToObject<JObject>()!.TryGetValue("Data", out var dataInnerNode))
            {
                var dailyData = JObject.Parse(dataInnerNode.ToString()).ToObject<DailyPowerUsageResult>();
                if (dailyData != null)
                {
                    return dailyData;
                }
                else
                {
                    throw new InvalidDataException("Cannot parse the HourlyData");
                }
            }
            else
            {
                throw new InvalidDataException("Cannot find the HourlyData");
            }
        }

        public async Task<HourlyPowerUsageResult> Hourly(DateTime startDate, DateTime endDate)
        {
            if (Account == null)
            {
                throw new Exception("You need to authenticate first");
            }

            var builder = new UriBuilder($"https://customerservice2api.southerncompany.com/api/MyPowerUsage/MPUData/{Account.AccountNumber}/Hourly");
            var query = HttpUtility.ParseQueryString(builder.Query);
            //query["params"] = "OPCO=GPC";
            query["StartDate"] = startDate.ToString("MM/dd/yyyy");
            query["EndDate"] = endDate.ToString("MM/dd/yyyy"); ;
            query["intervalBehavior"] = "Automatic";
            query["ServicePointNumber"] = Account.MeterServicePoint.ServicePointNumber;
            query["OPCO"] = Account.Company;

            builder.Query = query.ToString();
            string url = builder.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Bearer " + Jwt);

            var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Could not query MyPowerUsage/MPUData/AccountNumber/Hourly");
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            if (data.TryGetValue("Data", out var dataOuterNode) && dataOuterNode.ToObject<JObject>()!.TryGetValue("Data", out var dataInnerNode))
            {
                var hourlyData = JObject.Parse(dataInnerNode.ToString()).ToObject<HourlyPowerUsageResult>();
                if (hourlyData != null)
                {
                    return hourlyData;
                }
                else
                {
                    throw new InvalidDataException("Cannot parse the HourlyData");
                }
            }
            else
            {
                throw new InvalidDataException("Cannot find the HourlyData");
            }
        }

        private async Task<string> GetAccountInfo()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://customerservice2api.southerncompany.com/api/account/getAllAccounts");

            request.Headers.Add("Authorization", "Bearer " + Jwt);

            var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Could not query account/getAllAccounts");
            }

            var json = await response.Content.ReadAsStringAsync();

            var data = JObject.Parse(json);
            if (data.TryGetValue("Data", out var dataNode) && dataNode.Any() && dataNode.First()["AccountNumber"] != null)
            {
                return dataNode.First()["AccountNumber"]!.ToString();
            }
            else
            {
                throw new InvalidDataException("Cannot find the AccountNumber");
            }
        }

        private async Task<MeterServicePoint> GetServicePointNumber(string accountNumber)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://customerservice2api.southerncompany.com/api/MyPowerUsage/getMPUBasicAccountInformation/{accountNumber}/GPC");

            request.Headers.Add("Authorization", "Bearer " + Jwt);

            var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Could not query MyPowerUsage/getMPUBasicAccountInformation");
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);
            if (data.TryGetValue("Data", out var dataNode) && dataNode.ToObject<JObject>()!.TryGetValue("meterAndServicePoints", out var meterAndServicePoints))
            {
                var meterAndService = meterAndServicePoints.First().ToObject<MeterServicePoint>();
                if (meterAndService != null)
                {
                    return meterAndService;
                }
                else
                {
                    throw new InvalidDataException("Cannot parse the meterAndServicePoints");
                }
            }
            else
            {
                throw new InvalidDataException("Cannot find the meterAndServicePoints");
            }
        }

        private async Task<string> GetJwt(string scWebToken)
        {
            return await GetJwtFromScWebToken(scWebToken);
            //return await GetJwtScJwtToken(step1);
        }

        private async Task<string> GetJwtFromScWebToken(string scWebToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://customerservice2.southerncompany.com/Account/LoginComplete?ReturnUrl=/billing/home");

            var body = new Dictionary<string, string>()
            {
                {"ScWebToken", scWebToken}
            };

            request.Content = new FormUrlEncodedContent(body);

            var response = await Client.SendAsync(request);

            var cookies = response.Headers.SingleOrDefault(header => header.Key.Equals("Set-Cookie", StringComparison.InvariantCultureIgnoreCase)).Value;

            var southernJwtCookie = cookies.FirstOrDefault(o => o.StartsWith("ScJwtToken", StringComparison.InvariantCultureIgnoreCase));
            if (southernJwtCookie != null)
            {
                return southernJwtCookie.Split(";").First()["ScJwtToken=".Length..];
            }
            else
            {
                throw new InvalidDataException("Cannot find the ScJwtToken");
            }
        }

        private static async Task<string> GetVerificationToken()
        {
            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync("https://webauth.southernco.com/account/login");

            return doc.GetElementbyId("webauth-aft").GetAttributeValue<string>("data-aft", "");
        }

        private async Task<string> GetScWebToken(UserCredentials credentials, string verificationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://webauth.southernco.com/api/login");

            request.Headers.Add("RequestVerificationToken", verificationToken);

            var payload = new { username = credentials.Username, password = credentials.Password, targetPage = 1, @params = new { ReturnUrl = "null" } };
            request.Content = JsonContent.Create(payload);
            var result = await Client.SendAsync(request);
            var content = await result.Content.ReadFromJsonAsync<LoginResponse>();
            if (content != null)
            {

                var doc = new HtmlDocument();
                doc.LoadHtml(content.Data.Html);
                var scWebToken = doc.DocumentNode.SelectNodes(@"//input").Where(o => o.GetAttributes("NAME").Any(attr => attr != null && attr.Value == "ScWebToken") && o.GetAttributes("value").Any())
                    .Select(o => o.GetAttributeValue<string>("value", ""));

                return scWebToken.First();
            }
            else
            {
                throw new InvalidDataException("Cannot find the RequestVerificationToken");
            }
        }
    }
}
