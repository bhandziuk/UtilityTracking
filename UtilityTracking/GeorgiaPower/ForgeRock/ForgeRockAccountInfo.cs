namespace UtilityTracking.GeorgiaPower.ForgeRock;

public sealed class ForgeRockAccountInfo
{
    public string? UserSid { get; set; }
    /// <summary>Kept as a string to preserve account numbers, including leading zeroes.</summary>
    public string AccountNumber { get; set; } = string.Empty;
    public string? CssAccountNumber { get; set; }
    public string? C2MAccountNumber { get; set; }
    public string Company { get; set; } = string.Empty;
    public string? AccountType { get; set; }
    public string? Description { get; set; }
    public bool IsPrimaryAccount { get; set; }
    public bool IsCurrentViewAccount { get; set; }
    public bool IsPinRequired { get; set; }
    public bool IsPinValidated { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? AccountLockedUntil { get; set; }
}
