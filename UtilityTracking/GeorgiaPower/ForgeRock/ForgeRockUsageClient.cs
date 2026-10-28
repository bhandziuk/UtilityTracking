using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;

namespace UtilityTracking.GeorgiaPower.ForgeRock;

/// <summary>
/// Reads daily and hourly usage from the current OCC UsageGraphData API.
/// This is separate from the retired usage endpoints in GeorgiaPower.Requests.
/// </summary>
public sealed class ForgeRockUsageClient : IDisposable
{
    private const string UsageApiBase =
        "https://occmypowerusageapi.southerncompany.com/api/v1/MyPowerUsage/UsageGraphData";
    private const string PortalOrigin = "https://customerservice2.southerncompany.com";
    private const string BillingHomeUrl = PortalOrigin + "/Billing/Home";

    private readonly HttpClient _client;
    private bool _disposed;

    public ForgeRockUsageClient(string bearerToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(Requests.UserAgent);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _client.DefaultRequestHeaders.TryAddWithoutValidation("DeviceType", "Desktop");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", PortalOrigin);
        _client.DefaultRequestHeaders.Referrer = new Uri(BillingHomeUrl);
    }

    /// <summary>
    /// Gets hourly readings for an inclusive date range. The OCC endpoint's endDate is
    /// exclusive, so the requested end date is advanced by one day.
    /// </summary>
    public async Task<IReadOnlyList<ForgeRockHourlyUsage>> GetHourlyDataAsync(
        ForgeRockAccountSummary accountSummary,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveUsageContext(accountSummary);
        var graph = await GetUsageGraphAsync(
            context,
            "Hourly",
            startDate,
            endDate,
            cancellationToken);

        if (graph is null || IsNoData(graph.Value))
        {
            return [];
        }

        var usage = ReadSeries(graph.Value, "usage");
        var cost = ReadSeries(graph.Value, "cost");
        var temperature = ReadSeries(graph.Value, "temp");

        return GetLabels(graph.Value, usage, cost, temperature)
            .Select(label => new ForgeRockHourlyUsage(
                ParseGraphDate(label),
                usage.GetValueOrDefault(label),
                cost.GetValueOrDefault(label),
                temperature.GetValueOrDefault(label)))
            .ToArray();
    }

    /// <summary>
    /// Gets daily readings for an inclusive date range. The OCC endpoint's endDate is
    /// exclusive, so the requested end date is advanced by one day.
    /// </summary>
    public async Task<IReadOnlyList<ForgeRockDailyUsage>> GetDailyDataAsync(
        ForgeRockAccountSummary accountSummary,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveUsageContext(accountSummary);
        var graph = await GetUsageGraphAsync(
            context,
            "Daily",
            startDate,
            endDate,
            cancellationToken);

        if (graph is null || IsNoData(graph.Value))
        {
            return [];
        }

        var usage = ReadSeries(graph.Value, "usage");
        var cost = ReadSeries(graph.Value, "cost");
        var lowTemperature = ReadSeries(graph.Value, "lowtemp");
        var highTemperature = ReadSeries(graph.Value, "hightemp");

        return GetLabels(graph.Value, usage, cost, lowTemperature, highTemperature)
            .Select(label => new ForgeRockDailyUsage(
                ParseGraphDate(label),
                usage.GetValueOrDefault(label),
                cost.GetValueOrDefault(label),
                lowTemperature.GetValueOrDefault(label),
                highTemperature.GetValueOrDefault(label)))
            .ToArray();
    }

    private async Task<JsonElement?> GetUsageGraphAsync(
        UsageContext context,
        string granularity,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        if (endDate.Date < startDate.Date)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), "End date must be on or after start date.");
        }

        // The path segment is the server-issued serviceAgreementId (not the
        // customer-facing account number); accountId is a separate query value.
        var builder = new UriBuilder(
            $"{UsageApiBase}/{Uri.EscapeDataString(context.ServiceAgreementId)}/{granularity}");
        var query = HttpUtility.ParseQueryString(builder.Query);
        query["accountId"] = context.AccountId;
        query["personId"] = context.PersonId ?? string.Empty;
        query["operatingCompany"] = context.OperatingCompany;
        query["startDate"] = startDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        query["endDate"] = endDate.Date.AddDays(1).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        query["servicePointId"] = context.ServicePointId;
        query["premiseId"] = context.PremiseId ?? string.Empty;
        query["billFactorCode"] = "null";
        query["intervalBehavior"] = "Automatic";
        builder.Query = query.ToString();

        using var response = await _client.GetAsync(builder.Uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OCC {granularity.ToLowerInvariant()} usage request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (TryGetProperty(root, "status", out var status) &&
            status.ValueKind == JsonValueKind.False)
        {
            throw new InvalidDataException($"OCC {granularity.ToLowerInvariant()} usage response indicated failure.");
        }

        if (!TryGetProperty(root, "data", out var outerData) || outerData.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var graph = outerData;
        if (TryGetProperty(outerData, "data", out var innerData))
        {
            graph = innerData;
        }

        if (graph.ValueKind == JsonValueKind.String)
        {
            var json = graph.GetString();
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var nestedDocument = JsonDocument.Parse(json);
            return nestedDocument.RootElement.Clone();
        }

        return graph.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? graph.Clone()
            : null;
    }

    private static UsageContext ResolveUsageContext(ForgeRockAccountSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (string.IsNullOrWhiteSpace(summary.AccountId))
        {
            throw new InvalidDataException("Account summary did not contain accountId.");
        }

        var agreements = summary.ServiceAgreements;
        var agreement = agreements.FirstOrDefault(IsActiveElectricAgreement)
                        ?? agreements.FirstOrDefault(IsElectricAgreement)
                        ?? agreements.FirstOrDefault(item => item.IsActive)
                        ?? agreements.FirstOrDefault();

        if (agreement is null || string.IsNullOrWhiteSpace(agreement.ServiceAgreementId))
        {
            throw new InvalidDataException("Account summary did not contain a usable service agreement.");
        }

        var servicePoint = agreement.ServicePoints.FirstOrDefault(point =>
            !string.IsNullOrWhiteSpace(point.ServicePointId));

        if (servicePoint is null)
        {
            var badgeNumbers = agreement.ServicePoints
                .Select(point => point.BadgeNumber)
                .Where(badge => !string.IsNullOrWhiteSpace(badge))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matches = badgeNumbers.Count > 0
                ? summary.ServicePoints.Where(point =>
                    !string.IsNullOrWhiteSpace(point.ServicePointId) &&
                    point.BadgeNumber is not null &&
                    badgeNumbers.Contains(point.BadgeNumber)).ToArray()
                : [];

            if (matches.Length == 0 && agreements.Count == 1 && summary.ServicePoints.Count == 1)
            {
                matches = summary.ServicePoints
                    .Where(point => !string.IsNullOrWhiteSpace(point.ServicePointId))
                    .ToArray();
            }

            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    "Could not unambiguously resolve the servicePointId for the selected service agreement.");
            }

            servicePoint = matches[0];
        }

        if (string.IsNullOrWhiteSpace(servicePoint.ServicePointId))
        {
            throw new InvalidDataException("Selected service point did not contain servicePointId.");
        }

        return new UsageContext(
            summary.AccountId,
            summary.MainPersonId,
            FirstNonEmpty(summary.DivisionCode, "GPC"),
            agreement.ServiceAgreementId,
            agreement.PremiseId ?? string.Empty,
            servicePoint.ServicePointId);
    }

    private static bool IsActiveElectricAgreement(ForgeRockServiceAgreement agreement) =>
        agreement.IsActive && IsElectricAgreement(agreement);

    private static bool IsElectricAgreement(ForgeRockServiceAgreement agreement) =>
        string.Equals(agreement.ServiceTypeCode, "E", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(agreement.ServiceSubTypeCode, "E", StringComparison.OrdinalIgnoreCase) ||
        (agreement.ServiceSubTypeDescription?.Contains("electric", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string FirstNonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool IsNoData(JsonElement graph) =>
        TryGetProperty(graph, "hasData", out var hasData) && hasData.ValueKind == JsonValueKind.False;

    private static Dictionary<string, double> ReadSeries(JsonElement graph, params string[] names)
    {
        var points = new Dictionary<string, double>(StringComparer.Ordinal);
        if (!TryGetProperty(graph, "series", out var series) || series.ValueKind != JsonValueKind.Object)
        {
            return points;
        }

        var matchingSeries = series.EnumerateObject()
            .Where(item => !item.Name.Contains("projected", StringComparison.OrdinalIgnoreCase))
            .Where(item => names.Any(name => item.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Name.Contains("delayed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var seriesItem in matchingSeries)
        {
            var delayed = seriesItem.Name.Contains("delayed", StringComparison.OrdinalIgnoreCase);
            if (!TryGetProperty(seriesItem.Value, "data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var point in data.EnumerateArray())
            {
                if (!TryGetProperty(point, "name", out var nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String ||
                    !TryGetProperty(point, "y", out var valueElement) ||
                    !TryReadDouble(valueElement, out var value))
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name) || (delayed && value == 0))
                {
                    continue;
                }

                // Plain series take precedence; delayed series only fill gaps.
                points.TryAdd(name, value);
            }
        }

        return points;
    }

    private static IEnumerable<string> GetLabels(JsonElement graph, params Dictionary<string, double>[] series)
    {
        if (TryGetProperty(graph, "xAxis", out var xAxis) &&
            TryGetProperty(xAxis, "labels", out var labels) &&
            labels.ValueKind == JsonValueKind.Array)
        {
            var axisLabels = labels.EnumerateArray()
                .Where(label => label.ValueKind == JsonValueKind.String)
                .Select(label => label.GetString())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label!)
                .ToArray();
            if (axisLabels.Length > 0)
            {
                return axisLabels;
            }
        }

        return series.SelectMany(points => points.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
    }

    private static DateTime ParseGraphDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static bool TryReadDouble(JsonElement element, out double value)
    {
        value = default;
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDouble(out value);
        }

        return element.ValueKind == JsonValueKind.String &&
               double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _client.Dispose();
        _disposed = true;
    }

    private sealed record UsageContext(
        string AccountId,
        string? PersonId,
        string OperatingCompany,
        string ServiceAgreementId,
        string PremiseId,
        string ServicePointId);
}

public sealed record ForgeRockHourlyUsage(
    DateTime Date,
    double? Usage,
    double? Cost,
    double? Temperature);

public sealed record ForgeRockDailyUsage(
    DateTime Date,
    double? Usage,
    double? Cost,
    double? LowTemperature,
    double? HighTemperature);
