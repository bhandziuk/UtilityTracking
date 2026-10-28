using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtilityTracking.GeorgiaPower.ForgeRock;

public sealed class ForgeRockServicePoint
{
    public string? BadgeNumber { get; set; }
    /// <summary>Opaque server-issued identifier; resolve it from the current summary response.</summary>
    public string ServicePointId { get; set; } = string.Empty;
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
