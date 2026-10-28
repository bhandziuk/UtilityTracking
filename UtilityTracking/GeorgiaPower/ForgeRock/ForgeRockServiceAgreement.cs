using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtilityTracking.GeorgiaPower.ForgeRock;

public sealed class ForgeRockServiceAgreement
{
    public string ServiceAgreementId { get; set; } = string.Empty;
    public string? ServiceTypeCode { get; set; }
    public string? ServiceSubTypeCode { get; set; }
    public string? ServiceSubTypeDescription { get; set; }
    public string? StatusCode { get; set; }
    public string? StartDate { get; set; }
    public bool IsActive { get; set; }
    public string? PremiseId { get; set; }
    public string? PremiseAddress { get; set; }
    public string? Location { get; set; }
    public string? ServiceAgreementType { get; set; }
    public List<ForgeRockServicePoint> ServicePoints { get; set; } = [];
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
