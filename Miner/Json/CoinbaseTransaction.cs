using System.Text.Json.Serialization;

internal sealed class CoinbaseTransaction
{
    [JsonPropertyName("data")] public string? Data { get; set; }
    [JsonPropertyName("txid")] public string? TxId { get; set; }
}