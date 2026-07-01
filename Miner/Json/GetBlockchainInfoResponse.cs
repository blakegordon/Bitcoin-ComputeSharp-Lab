using System.Text.Json.Serialization;

internal sealed class GetBlockchainInfoResponse
{
    [JsonPropertyName("chain")] public string? Chain { get; set; }
    [JsonPropertyName("blocks")] public int Blocks { get; set; }
    [JsonPropertyName("headers")] public int Headers { get; set; }
}