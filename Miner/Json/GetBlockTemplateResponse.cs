using System.Text.Json.Serialization;

internal sealed class GetBlockTemplateResponse
{
    [JsonPropertyName("version")] public uint Version { get; set; }
    [JsonPropertyName("previousblockhash")] public string? PreviousBlockHash { get; set; }
    [JsonPropertyName("curtime")] public uint CurTime { get; set; }
    [JsonPropertyName("bits")] public string? Bits { get; set; }
    [JsonPropertyName("height")] public long Height { get; set; }
    [JsonPropertyName("coinbasetxn")] public CoinbaseTransaction? CoinbaseTxn { get; set; }
    [JsonPropertyName("transactions")] public TransactionTemplate[]? Transactions { get; set; }
    [JsonPropertyName("merkleroot")] public string? MerkleRoot { get; set; }

    // Required fields for SegWit Coinbase construction
    [JsonPropertyName("coinbasevalue")] public long CoinbaseValue { get; set; }
    [JsonPropertyName("default_witness_commitment")] public string? DefaultWitnessCommitment { get; set; }
}
