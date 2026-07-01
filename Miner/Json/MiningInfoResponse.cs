using System.Text.Json.Serialization;

internal sealed class MiningInfoResponse
{
    [JsonPropertyName("difficulty")] public double Difficulty { get; set; }
    [JsonPropertyName("networkhashps")] public double NetworkHashPs { get; set; }
}