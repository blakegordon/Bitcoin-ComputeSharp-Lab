using System.Text.Json.Serialization;

internal sealed class TransactionTemplate
{
    [JsonPropertyName("data")] public string? Data { get; set; }
    [JsonPropertyName("txid")] public string? TxId { get; set; }
    [JsonPropertyName("depends")] public long[]? Depends { get; set; }
    [JsonPropertyName("fee")] public long Fee { get; set; }
    [JsonPropertyName("weight")] public long Weight { get; set; }
}
