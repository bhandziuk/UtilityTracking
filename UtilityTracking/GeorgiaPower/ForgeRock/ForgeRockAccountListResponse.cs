using System.Text.Json;

namespace UtilityTracking.GeorgiaPower.ForgeRock;

public sealed class ForgeRockAccountListResponse
{
    public int StatusCode { get; set; }
    public bool Status { get; set; }
    public string? Message { get; set; }
    public List<ForgeRockAccountInfo>? Data { get; set; }
    public JsonElement ModelErrors { get; set; }
}
