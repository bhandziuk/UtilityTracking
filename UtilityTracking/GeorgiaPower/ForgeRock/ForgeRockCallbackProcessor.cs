using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UtilityTracking.GeorgiaPower.ForgeRock;

/// <summary>
/// Fills the username/password callbacks returned by Southern Company's ForgeRock journey.
/// This is separate from the legacy WebAuth login models and flow.
/// </summary>
public static partial class ForgeRockCallbackProcessor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void Fill(
        IList<ForgeRockCallback> callbacks,
        string username,
        string password)
    {
        foreach (var callback in callbacks)
        {
            var prompt = GetPrompt(callback);
            switch (callback.Type)
            {
                case "NameCallback":
                    if (MfaPromptRegex().IsMatch(prompt))
                    {
                        throw new InvalidOperationException(
                            "ForgeRock requested an additional authentication step (MFA/one-time code). " +
                            "This non-interactive login flow cannot complete that challenge.");
                    }
                    SetInput(callback, username);
                    break;

                case "PasswordCallback":
                    if (!string.IsNullOrWhiteSpace(prompt) && !PasswordPromptRegex().IsMatch(prompt))
                    {
                        throw new InvalidOperationException(
                            "ForgeRock requested an unsupported password callback.");
                    }
                    SetInput(callback, password);
                    break;

                case "ConfirmationCallback":
                    SetInput(callback, FindLoginChoice(callback));
                    break;

                case "DeviceProfileCallback":
                    SetInput(callback, CreateDeviceProfile());
                    break;

                case "BooleanAttributeInputCallback":
                    SetInput(callback, true);
                    break;

                case "TextOutputCallback":
                case "MetadataCallback":
                case "HiddenValueCallback":
                    break;

                default:
                    if (callback.Input.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"ForgeRock requested an unsupported authentication callback: {callback.Type}.");
                    }
                    break;
            }
        }
    }

    public static void ThrowIfRejected(IList<ForgeRockCallback> callbacks)
    {
        foreach (var callback in callbacks.Where(item =>
                     item.Type.Equals("TextOutputCallback", StringComparison.OrdinalIgnoreCase)))
        {
            var message = GetPrompt(callback);
            if (LoginFailureRegex().IsMatch(message))
            {
                // Do not include the server's message; it can contain account-specific data.
                throw new UnauthorizedAccessException(
                    "ForgeRock rejected the login. Verify the credentials or resolve the account notice in the portal.");
            }
        }
    }

    public static string Describe(ForgeRockCallback callback)
    {
        var inputNames = string.Join(",", callback.Input.Select(input => input.Name));
        var outputNames = string.Join(",", callback.Output.Select(output => output.Name));
        var inputsPopulated = callback.Input.Count > 0 && callback.Input.All(input =>
            input.Value.ValueKind switch
            {
                JsonValueKind.Undefined or JsonValueKind.Null => false,
                JsonValueKind.String => !string.IsNullOrEmpty(input.Value.GetString()),
                _ => true
            });

        var details = $"{callback.Type}(in=[{inputNames}],out=[{outputNames}],populated={inputsPopulated})";
        if (callback.Type.Equals("ConfirmationCallback", StringComparison.OrdinalIgnoreCase))
        {
            var options = callback.Output.FirstOrDefault(item =>
                item.Name.Equals("options", StringComparison.OrdinalIgnoreCase));
            var optionCount = options?.Value.ValueKind == JsonValueKind.Array
                ? options.Value.GetArrayLength()
                : 0;
            details += $"(optionCount={optionCount},selected={FindLoginChoice(callback)})";
        }

        return details;
    }

    private static void SetInput(ForgeRockCallback callback, object value)
    {
        if (callback.Input.Count == 0)
        {
            callback.Input.Add(new ForgeRockCallbackInput
            {
                Name = "IDToken1",
                Value = JsonSerializer.SerializeToElement(value, SerializerOptions)
            });
            return;
        }

        callback.Input[0].Value = JsonSerializer.SerializeToElement(value, SerializerOptions);
    }

    private static string GetPrompt(ForgeRockCallback callback)
    {
        var output = callback.Output.FirstOrDefault(item =>
            item.Name.Equals("prompt", StringComparison.OrdinalIgnoreCase) ||
            item.Name.Equals("message", StringComparison.OrdinalIgnoreCase));

        return output?.Value.ValueKind == JsonValueKind.String
            ? output.Value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int FindLoginChoice(ForgeRockCallback callback)
    {
        var options = callback.Output.FirstOrDefault(item =>
            item.Name.Equals("options", StringComparison.OrdinalIgnoreCase));

        if (options?.Value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var option in options.Value.EnumerateArray())
            {
                if (option.ValueKind == JsonValueKind.String &&
                    LoginChoiceRegex().IsMatch(option.GetString() ?? string.Empty))
                {
                    return index;
                }
                index++;
            }
        }

        // ForgeRock's default option is sometimes Cancel; the login action is usually first.
        return 0;
    }

    private static object CreateDeviceProfile() => new
    {
        identifier = "utility-tracking",
        metadata = new
        {
            platform = new { platform = "Win32", userAgent = Requests.UserAgent, language = "en-US" },
            browser = new { userAgent = Requests.UserAgent, appName = "Netscape", language = "en-US" },
            hardware = new { cpuClass = (string?)null, deviceMemory = 8, hardwareConcurrency = 8, maxTouchPoints = 0 }
        }
    };

    [GeneratedRegex("passcode|otp|one[- ]time|verification code|mfa", RegexOptions.IgnoreCase)]
    private static partial Regex MfaPromptRegex();

    [GeneratedRegex("pass", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordPromptRegex();

    [GeneratedRegex("log\\s*in|login|submit|continue|next", RegexOptions.IgnoreCase)]
    private static partial Regex LoginChoiceRegex();

    [GeneratedRegex("invalid|incorrect|failed|locked|not recognized", RegexOptions.IgnoreCase)]
    private static partial Regex LoginFailureRegex();
}

public sealed class ForgeRockResponse
{
    [JsonPropertyName("tokenId")]
    public string? TokenId { get; set; }

    [JsonPropertyName("successUrl")]
    public string? SuccessUrl { get; set; }

    [JsonPropertyName("authId")]
    public string? AuthId { get; set; }

    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("callbacks")]
    public List<ForgeRockCallback> Callbacks { get; set; } = [];

    // ForgeRock includes journey metadata (for example stage/description) that
    // must survive the callback round-trip for this portal's login journey.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class ForgeRockCallback
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public List<ForgeRockCallbackOutput> Output { get; set; } = [];

    [JsonPropertyName("input")]
    public List<ForgeRockCallbackInput> Input { get; set; } = [];

    // Keep ForgeRock callback identity fields such as `_id` when submitting
    // the callback list back to the authenticate endpoint.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class ForgeRockCallbackOutput
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class ForgeRockCallbackInput
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
