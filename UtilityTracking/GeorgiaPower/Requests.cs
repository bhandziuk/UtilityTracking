using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace UtilityTracking.GeorgiaPower
{
    public class Requests
    {
        private readonly HttpClient Client;
        private readonly string? ServicePointNumber;
        private string Jwt;
        private string AccountNumber;

        public Requests(HttpClient client)
        {
            Client = client;
        }

        public async Task<PowerUsageResult?> Hourly(DateTime startDate, DateTime endDate)
        {
            var builder = new UriBuilder($"https://customerservice2api.southerncompany.com/api/MyPowerUsage/MPUData/{AccountNumber}/Hourly");
            var query = HttpUtility.ParseQueryString(builder.Query);
            //query["params"] = "OPCO=GPC";
            query["StartDate"] = startDate.ToString("MM/dd/yyyy");
            query["EndDate"] = endDate.ToString("MM/dd/yyyy"); ;
            query["intervalBehavior"] = "Automatic";
            query["ServicePointNumber"] = ServicePointNumber;
            query["OPCO"] = "GPC";

            builder.Query = query.ToString();
            string url = builder.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Bearer " + Jwt);

            var response = await Client.SendAsync(request);

            var data = await response.Content.ReadAsStringAsync();

            return await response.Content.ReadFromJsonAsync<PowerUsageResult>();
        }

        public async Task Authenticate(UserCredentials credentials)
        {
            var verificationToken = await GetVerificationToken();
            var scWebToken = await GetScWebToken(credentials, verificationToken);
            Jwt = await GetJwt(scWebToken);
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

            return southernJwtCookie.Split(";").First().Substring("ScJwtToken=".Length);
        }

        private async Task<string> GetJwtScJwtToken(string southernJwt)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "$https://customerservice2api.southerncompany.com/api/account/getProductPortalJwt/{AccountNumber}");

            request.Headers.Add("Authorization", "Bearer " + southernJwt);

            var response = await Client.SendAsync(request);

            return "";
        }

        private async Task<string> GetVerificationToken()
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

            var doc = new HtmlDocument();
            doc.LoadHtml(content.Data.Html);
            var scWebToken = doc.DocumentNode.SelectNodes(@"//input").Where(o => o.GetAttributes("NAME").Any(attr => attr != null && attr.Value == "ScWebToken") && o.GetAttributes("value").Any())
                .Select(o => o.GetAttributeValue<string>("value", ""));

            return scWebToken.First();
        }

        public Task<HttpResponseMessage> Login(UserCredentials credentials)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://webauth.southernco.com/webservices/api/WebUser/Login");

            request.Headers.Add("Referer", "https://webauth.southernco.com/SPA/OCC/login");

            var payload = new { username = credentials.Username, password = credentials.Password, applicationType = "E", applicationId = "OCC", company = "SCS" };
            request.Content = JsonContent.Create(payload);

            return Client.SendAsync(request);
        }
    }
}
