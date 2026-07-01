using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class BitcoinRpcClient : IDisposable
{
    private static readonly object?[] EmptyParameters = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object?[] GetBlockTemplateParameters =
    [
        new
        {
            mode = "template",
            capabilities = new string[] { "coinbasetxn", "workid", "coinbase/append" },
            rules = new string[] { "segwit", "taproot" }
        }
    ];

    private readonly HttpClient _httpClient;
    private readonly Uri _uri;
    private readonly string? _authHeaderValue;

    private BitcoinRpcClient(Uri rpcUrl, string? rpcUser, string? rpcPassword)
    {
        _uri = rpcUrl;
        _httpClient = new HttpClient(new HttpClientHandler { Proxy = null, UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrWhiteSpace(rpcUser) && !string.IsNullOrWhiteSpace(rpcPassword))
        {
            string raw = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{rpcUser}:{rpcPassword}"));
            _authHeaderValue = $"Basic {raw}";
        }
    }

    public static async Task<BitcoinRpcClient> CreateAsync(MiningOptions options)
    {
        string? user = options.RpcUser;
        string? password = options.RpcPassword;

        if (!string.IsNullOrWhiteSpace(options.CookiePath))
        {
            if (File.Exists(options.CookiePath))
            {
                (user, password) = await ReadCredentialsFromFileAsync(
                    options.CookiePath,
                    "Bitcoin Core auth cookie",
                    "__cookie__:randomtoken").ConfigureAwait(false);
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.RpcAuthFilePath))
        {
            (user, password) = await ReadCredentialsFromFileAsync(
                options.RpcAuthFilePath,
                "RPC credentials",
                "username:password").ConfigureAwait(false);
        }

        return new BitcoinRpcClient(options.RpcUrl, user, password);
    }

    public async Task<GetBlockchainInfoResponse> GetBlockchainInfoAsync(CancellationToken cancellationToken)
    {
        return await SendAsync<GetBlockchainInfoResponse>("getblockchaininfo", cancellationToken).ConfigureAwait(false);
    }

    public async Task<MiningInfoResponse> GetMiningInfoAsync(CancellationToken cancellationToken)
    {
        return await SendAsync<MiningInfoResponse>("getmininginfo", cancellationToken).ConfigureAwait(false);
    }

    public async Task<GetBlockTemplateResponse> GetBlockTemplateAsync(CancellationToken cancellationToken)
    {
        return await SendAsync<GetBlockTemplateResponse>("getblocktemplate", GetBlockTemplateParameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> SubmitBlockAsync(string hex, CancellationToken cancellationToken)
    {
        JsonElement payload = await SendAsync<JsonElement>("submitblock", hex, cancellationToken).ConfigureAwait(false);
        if (payload.ValueKind == JsonValueKind.String)
        {
            return payload.GetString();
        }

        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("result", out JsonElement result))
        {
            return result.ValueKind == JsonValueKind.String ? result.GetString() : result.ToString();
        }

        return null;
    }

    public async Task<DecodeScriptResponse> DecodeScriptAsync(string hex, CancellationToken cancellationToken)
    {
        return await SendAsync<DecodeScriptResponse>("decodescript", hex, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string User, string Password)> ReadCredentialsFromFileAsync(string path, string kind, string expectedFormat)
    {
        if (!File.Exists(path))
        {
            throw new ApplicationException($"{kind} file not found at '{path}'.");
        }

        string text = (await File.ReadAllTextAsync(path).ConfigureAwait(false)).Trim();
        int idx = text.IndexOf(':');
        if (idx <= 0 || idx >= text.Length - 1)
        {
            throw new ApplicationException($"{kind} file is malformed. Expected one line in format: {expectedFormat}");
        }

        return (text[..idx], text[(idx + 1)..]);
    }

    private async Task<T> SendAsync<T>(string method, CancellationToken cancellationToken)
    {
        return await SendAsync<T>(method, EmptyParameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(string method, object? parameter, CancellationToken cancellationToken)
    {
        return await SendAsync<T>(method, [parameter], cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(string method, object?[] parameters, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, _uri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id = Guid.NewGuid().ToString("N"),
                        method,
                        @params = parameters
                    },
                    JsonOptions),
                Encoding.UTF8,
                new MediaTypeHeaderValue("application/json"))
        };

        if (!string.IsNullOrWhiteSpace(_authHeaderValue))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeaderValue.Split(' ', 2)[1]);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"RPC request failed ({(int)response.StatusCode}): {body}");
        }

        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind != JsonValueKind.Null)
        {
            string msg = error.TryGetProperty("message", out JsonElement message) ? message.GetString() ?? error.ToString() : error.ToString();
            throw new InvalidOperationException($"Bitcoin Core RPC error: {msg}");
        }

        if (!document.RootElement.TryGetProperty("result", out JsonElement result))
        {
            throw new InvalidOperationException("RPC response did not contain a result.");
        }

        return result.Deserialize<T>(JsonOptions)!;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
