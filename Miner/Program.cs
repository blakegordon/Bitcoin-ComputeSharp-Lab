using ComputeSharp;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

internal static class Program
{
    const int LabelWidth = 30;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            MiningOptions options = MiningOptions.Parse(args);

            string authMode =
                !string.IsNullOrWhiteSpace(options.CookiePath) ? $"using auth cookie" :
                !string.IsNullOrWhiteSpace(options.RpcAuthFilePath) ? $"rpc-auth-file '{options.RpcAuthFilePath}'" :
                "username/password";

            using BitcoinRpcClient rpc = await BitcoinRpcClient.CreateAsync(options).ConfigureAwait(false);
            GetBlockchainInfoResponse info = await rpc.GetBlockchainInfoAsync(CancellationToken.None).ConfigureAwait(false);
            MiningInfoResponse miningInfo = await rpc.GetMiningInfoAsync(CancellationToken.None).ConfigureAwait(false);
            DecodeScriptResponse decodedScript = await rpc.DecodeScriptAsync(options.PayoutScriptHex, CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine($"{"Connected to Bitcoin Core:",-LabelWidth} {options.RpcUrl} ({authMode})");
            Console.WriteLine($"  {"Chain:",-LabelWidth + 2} {info.Chain}");
            Console.WriteLine($"  {"Blocks:",-LabelWidth + 2} {info.Blocks:N0}");
            Console.WriteLine($"  {"Headers:",-LabelWidth + 2} {info.Headers:N0}");
            Console.WriteLine($"  {"Difficulty:",-LabelWidth + 2} {miningInfo.Difficulty / 1e12:F2} trillion times harder than in 2009");
            Console.WriteLine($"  {"Network Hashrate:",-LabelWidth + 2} {miningInfo.NetworkHashPs / 1e18:F2} exahashes per second");
            Console.WriteLine($"  {"Payout address:",-LabelWidth + 2} {decodedScript.Address ?? options.PayoutScriptHex} ({decodedScript.Type})");

            GraphicsDevice[] devices = [.. GraphicsDevice.QueryDevices(static d => d.IsHardwareAccelerated)];

            if (devices.Length == 0)
            {
                Console.Error.WriteLine("No accelerated GPU devices were found. Exiting.");
                return 1;
            }

            int maxDevices = Math.Max(1, Math.Min(options.MaxGpuDevices, devices.Length));
            GraphicsDevice[] selectedDevices = [.. devices.Take(maxDevices)];

            Console.WriteLine();
            Console.WriteLine($"Using {selectedDevices.Length} GPU device(s):");
            for (int i = 0; i < selectedDevices.Length; i++)
            {
                Console.WriteLine($"  [{i}] {selectedDevices[i].Name}");
            }

            using CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Task[] workers =
            [
                .. selectedDevices.Select((device, index) => Task.Run(() => new GpuMiningWorker(rpc, options, device, index).RunAsync(cts.Token), cts.Token))
            ];

            await Task.WhenAll(workers).ConfigureAwait(false);

            return 0;
        }
        catch (Exception ex) when (ex is ApplicationException || ex is HttpRequestException)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 2;
        }
    }
}
