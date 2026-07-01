using System.Text.Json.Serialization;

internal sealed class DecodeScriptResponse
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
}
