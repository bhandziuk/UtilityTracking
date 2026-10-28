using HtmlAgilityPack;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using UtilityTracking.GeorgiaPower.ForgeRock;

namespace UtilityTracking.GeorgiaPower;

/// <summary>
/// Coordinates ForgeRock sign-in, OCC account discovery, and usage retrieval.
/// The retired WebAuth login and customerservice2api calls have been removed.
/// </summary>
public sealed partial class Requests : IDisposable
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                                    "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private const string PortalHost = "customerservice2.southerncompany.com";
    private const string LoginHost = "customerlogin.southernco.com";
    private const string FallbackSsoCookieName = "c4928758b74fd64";
    private const string ServerInfoUrl = "https://customerlogin.southernco.com/am/json/realms/root/realms/alpha/serverinfo/*";
    private const string JwtUrl = "https://customerservice2.southerncompany.com/Account/LoginValidated/JwtToken";
    private const string BillingHomeUrl = "https://customerservice2.southerncompany.com/Billing/Home";
    private const string AccountApiBase = "https://occaccountapi.southerncompany.com/api/v1";
    private const int MaxRedirects = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _apiClient;
    private readonly CookieContainer _authCookies;
    private readonly HttpClient _authClient;
    private bool _disposed;
    private string? Jwt;
    private ForgeRockAccountSummary? Summary;

    public Account? Account { get; private set; }

    public Requests(HttpClient client)
    {
        _apiClient = client;
        _authCookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = _authCookies,
            AutomaticDecompression = DecompressionMethods.All
        };

        _authClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _authClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _authClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _authClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));
    }

    /// <summary>Signs in, chooses the primary account, and loads its OCC summary.</summary>
    public async Task Authenticate(
        UserCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(credentials);

        Jwt = await AuthenticateForgeRockAsync(credentials, cancellationToken);
        var accounts = await GetAccountsAsync(cancellationToken);
        var selectedAccount = accounts.FirstOrDefault(account => account.IsPrimaryAccount)
                              ?? accounts.FirstOrDefault();

        if (selectedAccount is null)
        {
            throw new InvalidDataException("The OCC account list did not contain any accounts.");
        }

        Summary = await GetAccountSummaryAsync(selectedAccount.AccountNumber, cancellationToken);
        var servicePointId = ResolveAccountServicePoint(Summary);
        var company = string.IsNullOrWhiteSpace(Summary.DivisionCode)
            ? selectedAccount.Company
            : Summary.DivisionCode;

        Account = new Account(selectedAccount.AccountNumber, servicePointId, company);
    }

    public async Task<IReadOnlyList<ForgeRockAccountInfo>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        return await GetOccDataAsync<List<ForgeRockAccountInfo>>(
            $"{AccountApiBase}/Cap/",
            "OCC account list",
            cancellationToken);
    }

    public async Task<ForgeRockAccountSummary> GetAccountSummaryAsync(
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);

        return await GetOccDataAsync<ForgeRockAccountSummary>(
            $"{AccountApiBase}/Accounts/{Uri.EscapeDataString(accountNumber)}/Summary",
            "OCC account summary",
            cancellationToken);
    }

    public async Task<DailyPowerUsageResult> Daily(DateTime startDate, DateTime endDate)
    {
        EnsureUsageReady();
        using var usageClient = new ForgeRockUsageClient(Jwt!);
        var readings = await usageClient.GetDailyDataAsync(Summary!, startDate, endDate);
        return ForgeRockUsageResultMapper.ToDailyPowerUsageResult(readings);
    }

    public async Task<HourlyPowerUsageResult> Hourly(DateTime startDate, DateTime endDate)
    {
        EnsureUsageReady();
        using var usageClient = new ForgeRockUsageClient(Jwt!);
        var readings = await usageClient.GetHourlyDataAsync(Summary!, startDate, endDate);
        return ForgeRockUsageResultMapper.ToHourlyPowerUsageResult(readings);
    }

    private async Task<T> GetOccDataAsync<T>(
        string url,
        string operation,
        CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.TryAddWithoutValidation("DeviceType", "Desktop");
        request.Headers.TryAddWithoutValidation("Origin", "https://customerservice2.southerncompany.com");
        request.Headers.Referrer = new Uri(BillingHomeUrl);

        using var response = await _apiClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{operation} request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        OccApiEnvelope<T>? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<OccApiEnvelope<T>>(
                stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"{operation} returned an unrecognized JSON response.");
        }

        if (envelope is null || !envelope.Status || envelope.Data is null)
        {
            throw new InvalidDataException($"{operation} response indicated failure or contained no data.");
        }

        return envelope.Data;
    }

    private static string ResolveAccountServicePoint(ForgeRockAccountSummary summary)
    {
        var agreement = summary.ServiceAgreements.FirstOrDefault(IsActiveElectricAgreement)
                        ?? summary.ServiceAgreements.FirstOrDefault(IsElectricAgreement)
                        ?? summary.ServiceAgreements.FirstOrDefault(item => item.IsActive)
                        ?? summary.ServiceAgreements.FirstOrDefault();

        var servicePointId = agreement?.ServicePoints
            .FirstOrDefault(point => !string.IsNullOrWhiteSpace(point.ServicePointId))?.ServicePointId;

        servicePointId ??= summary.ServicePoints
            .FirstOrDefault(point => !string.IsNullOrWhiteSpace(point.ServicePointId))?.ServicePointId;

        return servicePointId ?? throw new InvalidDataException(
            "Account summary did not contain a usable service point.");
    }

    private static bool IsActiveElectricAgreement(ForgeRockServiceAgreement agreement) =>
        agreement.IsActive && IsElectricAgreement(agreement);

    private static bool IsElectricAgreement(ForgeRockServiceAgreement agreement) =>
        string.Equals(agreement.ServiceTypeCode, "E", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(agreement.ServiceSubTypeCode, "E", StringComparison.OrdinalIgnoreCase) ||
        (agreement.ServiceSubTypeDescription?.Contains("electric", StringComparison.OrdinalIgnoreCase) ?? false);

    private void EnsureAuthenticated()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(Jwt))
        {
            throw new InvalidOperationException("Authenticate with the ForgeRock flow first.");
        }
    }

    private void EnsureUsageReady()
    {
        EnsureAuthenticated();
        if (Summary is null)
        {
            throw new InvalidOperationException("Authenticate and load an account summary before requesting usage.");
        }
    }

    private async Task<string> AuthenticateForgeRockAsync(
        UserCredentials credentials,
        CancellationToken cancellationToken)
    {
        var authorizeUri = await FindAuthorizeUriAsync(cancellationToken);
        var cookieName = await GetSsoCookieNameAsync(cancellationToken);
        var tokenId = await RunForgeRockJourneyAsync(authorizeUri, credentials, cancellationToken);

        _authCookies.Add(
            new Uri($"https://{LoginHost}/"),
            new Cookie(cookieName, tokenId, "/", LoginHost)
            {
                Secure = true,
                HttpOnly = true
            });

        var responseJwt = await CompletePortalSessionAsync(authorizeUri, cancellationToken);
        return responseJwt ?? await GetJwtFromPortalAsync(cancellationToken);
    }

    private async Task<Uri> FindAuthorizeUriAsync(CancellationToken cancellationToken)
    {
        var startUri = new Uri(
            $"https://{PortalHost}/Account/Login" +
            $"?returnUrl={Uri.EscapeDataString("/Billing/Home")}" +
            $"&cancelUrl={Uri.EscapeDataString("/Home")}" +
            "&company=GPC&appId=pr-occ-forgerock");

        var current = startUri;
        Uri? authorizeUri = null;
        for (var hop = 0; hop < MaxRedirects; hop++)
        {
            using var response = await SendAuthAsync(HttpMethod.Get, current, cancellationToken: cancellationToken);
            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                var next = ResolveUri(current, location);
                authorizeUri = FindAuthorizeUri(next) ?? authorizeUri;
                current = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Portal login bootstrap failed with HTTP {(int)response.StatusCode} at {SafePath(current)}.");
            }

            break;
        }

        return authorizeUri ?? throw new InvalidDataException(
            "Portal login did not redirect to the ForgeRock OAuth authorize endpoint.");
    }

    private static Uri? FindAuthorizeUri(Uri candidate, int depth = 0)
    {
        if (candidate.Host.Equals(LoginHost, StringComparison.OrdinalIgnoreCase) &&
            candidate.AbsolutePath.Contains("/oauth2/authorize", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (depth >= 4 || string.IsNullOrEmpty(candidate.Query))
        {
            return null;
        }

        foreach (var parameter in candidate.Query.TrimStart('?').Split('&'))
        {
            var separator = parameter.IndexOf('=');
            if (separator < 0 || separator == parameter.Length - 1)
            {
                continue;
            }

            var encodedValue = parameter[(separator + 1)..].Replace("+", "%20", StringComparison.Ordinal);
            var decodedValue = Uri.UnescapeDataString(encodedValue);
            if (Uri.TryCreate(decodedValue, UriKind.Absolute, out var nestedUri))
            {
                var found = FindAuthorizeUri(nestedUri, depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private async Task<string> GetSsoCookieNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ServerInfoUrl);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Accept-API-Version", "resource=1.1");

            using var response = await _authClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FallbackSsoCookieName;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("cookieName", out var cookieName) &&
                cookieName.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(cookieName.GetString()))
            {
                return cookieName.GetString()!;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Fall back to the currently observed ForgeRock session-cookie name.
        }

        return FallbackSsoCookieName;
    }

    private async Task<string> RunForgeRockJourneyAsync(
        Uri authorizeUri,
        UserCredentials credentials,
        CancellationToken cancellationToken)
    {
        var authUri = new Uri(
            $"https://{LoginHost}/am/json/realms/root/realms/alpha/authenticate" +
            "?authIndexType=service&authIndexValue=occSecureLogin" +
            $"&goto={Uri.EscapeDataString(authorizeUri.AbsoluteUri)}");

        string? nextBody = null;
        string? previousAuthId = null;
        var exchangeTrace = new List<string>();
        for (var round = 0; round < 6; round++)
        {
            using var content = nextBody is null
                ? null
                : new StringContent(nextBody, Encoding.UTF8, "application/json");

            using var response = await SendAuthAsync(
                HttpMethod.Post,
                authUri,
                content,
                new Uri($"https://{LoginHost}/am/XUI/"),
                cancellationToken,
                acceptJson: true);

            var payload = await ReadForgeRockJsonAsync(response, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload.TokenId))
            {
                return payload.TokenId;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || payload.Code == 401)
            {
                throw new UnauthorizedAccessException("ForgeRock rejected the login. Verify credentials or resolve account notices in the portal.");
            }

            if (string.IsNullOrWhiteSpace(payload.AuthId) || payload.Callbacks.Count == 0)
            {
                throw new InvalidDataException(
                    $"ForgeRock returned an unexpected response (HTTP {(int)response.StatusCode}); no session token or login callbacks were present. " +
                    $"Previous callback types: {FormatTrace(exchangeTrace)}.");
            }

            ForgeRockCallbackProcessor.ThrowIfRejected(payload.Callbacks);
            ForgeRockCallbackProcessor.Fill(payload.Callbacks, credentials.Username, credentials.Password);

            var authIdChanged = previousAuthId is null
                ? "first"
                : (payload.AuthId.Equals(previousAuthId, StringComparison.Ordinal) ? "no" : "yes");
            var cookieNames = _authCookies.GetCookies(authUri)
                .Cast<Cookie>()
                .Select(cookie => cookie.Name)
                .OrderBy(name => name, StringComparer.Ordinal);
            exchangeTrace.Add(
                $"HTTP {(int)response.StatusCode},authIdChanged={authIdChanged},cookies=[{string.Join(",", cookieNames)}]: " +
                string.Join(";", payload.Callbacks.Select(ForgeRockCallbackProcessor.Describe)));
            previousAuthId = payload.AuthId;

            var continuation = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (payload.AdditionalData is not null)
            {
                foreach (var (name, value) in payload.AdditionalData)
                {
                    continuation[name] = value;
                }
            }
            continuation["authId"] = payload.AuthId;
            continuation["callbacks"] = payload.Callbacks;
            nextBody = JsonSerializer.Serialize(continuation, JsonOptions);
        }

        throw new InvalidOperationException(
            "ForgeRock authentication did not complete after six callback exchanges. " +
            $"Observed callback types: {FormatTrace(exchangeTrace)}.");
    }

    private static string FormatTrace(IEnumerable<string> trace) =>
        trace.Any() ? string.Join(" -> ", trace) : "none";

    private async Task<string?> CompletePortalSessionAsync(Uri authorizeUri, CancellationToken cancellationToken)
    {
        var currentUri = authorizeUri;
        HttpResponseMessage? response = await SendAuthAsync(HttpMethod.Get, currentUri, cancellationToken: cancellationToken);
        try
        {
            for (var hop = 0; hop < MaxRedirects; hop++)
            {
                var jwt = ExtractJwt(response);
                if (jwt is not null)
                {
                    return jwt;
                }

                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
                {
                    var nextUri = ResolveUri(currentUri, location);
                    response.Dispose();
                    currentUri = nextUri;
                    response = await SendAuthAsync(HttpMethod.Get, currentUri, cancellationToken: cancellationToken);
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync(cancellationToken);
                    var form = TryReadOidcForm(html);
                    if (form is not null)
                    {
                        var oidcForm = form.Value;
                        var action = string.IsNullOrWhiteSpace(oidcForm.Action)
                            ? currentUri
                            : ResolveUri(currentUri, new Uri(oidcForm.Action, UriKind.RelativeOrAbsolute));
                        var referer = currentUri;
                        response.Dispose();

                        using var content = new FormUrlEncodedContent(oidcForm.Fields);
                        response = await SendAuthAsync(HttpMethod.Post, action, content, referer, cancellationToken);
                        currentUri = action;
                        continue;
                    }
                }

                break;
            }
        }
        finally
        {
            response?.Dispose();
        }

        return null;
    }

    private async Task<string> GetJwtFromPortalAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, JwtUrl);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Referrer = new Uri(BillingHomeUrl);

        using var response = await _authClient.SendAsync(request, cancellationToken);
        var jwt = ExtractJwt(response);
        if (jwt is not null)
        {
            return jwt;
        }

        var headerNames = response.Headers.Select(header => header.Key).OrderBy(name => name);
        throw new InvalidDataException(
            $"JwtToken returned HTTP {(int)response.StatusCode} without a recognized JWT header or cookie. " +
            $"Response headers: {string.Join(", ", headerNames)}.");
    }

    private async Task<HttpResponseMessage> SendAuthAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content = null,
        Uri? referer = null,
        CancellationToken cancellationToken = default,
        bool acceptJson = false)
    {
        EnsureTrustedUri(uri);
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Accept.Clear();
        if (acceptJson)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Accept-API-Version", "resource=2.1, protocol=1.0");
            request.Headers.TryAddWithoutValidation("Origin", $"https://{LoginHost}");
        }
        else
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
        }

        if (referer is not null)
        {
            request.Headers.Referrer = referer;
        }

        return await _authClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    private static async Task<ForgeRockResponse> ReadForgeRockJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<ForgeRockResponse>(stream, JsonOptions, cancellationToken)
                   ?? throw new JsonException("The response was empty.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException(
                $"ForgeRock authenticate returned a non-JSON or unrecognized response (HTTP {(int)response.StatusCode}).");
        }
    }

    private static string? ExtractJwt(HttpResponseMessage response)
    {
        foreach (var headerName in new[] { "ScJwtToken", "ScSoftAuthJwtToken", "ScAccountsJwtToken" })
        {
            if (response.Headers.TryGetValues(headerName, out var values))
            {
                var value = values.FirstOrDefault()?.Trim();
                if (IsJwt(value))
                {
                    return value;
                }
            }
        }

        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                var separator = cookie.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = cookie[..separator].Trim();
                if (!name.Equals("ScJwtToken", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("ScSoftAuthJwtToken", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("ScAccountsJwtToken", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = cookie[(separator + 1)..].Split(';', 2)[0].Trim();
                if (IsJwt(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool IsJwt(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Split('.') is { Length: 3 } parts &&
        parts.All(part => part.Length > 0);

    private static (string Action, Dictionary<string, string> Fields)? TryReadOidcForm(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var forms = document.DocumentNode.SelectNodes("//form");
        if (forms is null)
        {
            return null;
        }

        foreach (var form in forms)
        {
            if (form.SelectSingleNode(".//input[translate(@type, 'PASSWORD', 'password')='password']") is not null)
            {
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var inputs = form.SelectNodes(".//input");
            if (inputs is null)
            {
                continue;
            }

            foreach (var input in inputs)
            {
                var name = input.GetAttributeValue("name", string.Empty);
                var type = input.GetAttributeValue("type", "hidden");
                if (string.IsNullOrWhiteSpace(name) ||
                    (!type.Equals("hidden", StringComparison.OrdinalIgnoreCase) &&
                     !type.Equals("submit", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                fields[name] = input.GetAttributeValue("value", string.Empty);
            }

            if (fields.ContainsKey("code") || fields.ContainsKey("state") ||
                fields.ContainsKey("ScWebToken") || fields.ContainsKey("Token") || fields.ContainsKey("id_token"))
            {
                return (form.GetAttributeValue("action", string.Empty), fields);
            }
        }

        return null;
    }

    private static Uri ResolveUri(Uri current, Uri location)
    {
        var resolved = location.IsAbsoluteUri ? location : new Uri(current, location);
        EnsureTrustedUri(resolved);
        return resolved;
    }

    private static void EnsureTrustedUri(Uri uri)
    {
        var host = uri.Host;
        var trustedHost = host.Equals("southernco.com", StringComparison.OrdinalIgnoreCase) ||
                          host.EndsWith(".southernco.com", StringComparison.OrdinalIgnoreCase) ||
                          host.Equals("southerncompany.com", StringComparison.OrdinalIgnoreCase) ||
                          host.EndsWith(".southerncompany.com", StringComparison.OrdinalIgnoreCase);

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !trustedHost)
        {
            throw new InvalidDataException("The login flow returned a redirect outside trusted Southern Company HTTPS domains.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode is >= 300 and < 400;

    private static string SafePath(Uri uri) => $"{uri.Host}{uri.AbsolutePath}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _authClient.Dispose();
        _disposed = true;
    }

    private sealed class OccApiEnvelope<T>
    {
        public int StatusCode { get; set; }
        public bool Status { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public JsonElement ModelErrors { get; set; }
    }
}
