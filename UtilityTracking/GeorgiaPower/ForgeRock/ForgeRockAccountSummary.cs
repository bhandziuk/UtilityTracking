using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtilityTracking.GeorgiaPower.ForgeRock;

public sealed class ForgeRockAccountSummary
{
    public string AccountId { get; set; } = string.Empty;
    public string? DivisionCode { get; set; }
    public string? MainPersonId { get; set; }
    public string? CustomerName { get; set; }
    public string? AccessGroupCode { get; set; }
    public string? CustomerClassCode { get; set; }
    public string? AccountOpenedDate { get; set; }
    public string? AccountType { get; set; }
    public string? AccountStatus { get; set; }
    public string? PrePayStatus { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? AutoPayPaymentTypeDescription { get; set; }
    public string? AutoPayEndDate { get; set; }
    public string? AutoPayExpireDate { get; set; }
    public string? AutoPayStartDate { get; set; }
    public bool IsPermanentBillIntercept { get; set; }
    public bool IsSummaryBilling { get; set; }
    public string? BillRouteTypeCode { get; set; }
    public string? Barcode { get; set; }
    public string? ServiceAddress { get; set; }
    public List<JsonElement> ContactDetails { get; set; } = [];
    public List<ForgeRockServiceAgreement> ServiceAgreements { get; set; } = [];
    public List<ForgeRockServicePoint> ServicePoints { get; set; } = [];
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
