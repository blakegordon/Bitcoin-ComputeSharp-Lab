using System.Globalization;

internal sealed record MiningOptions
{
    public Uri RpcUrl { get; init; } = new Uri("http://127.0.0.1:8332");
    public string? RpcUser { get; init; }
    public string? RpcPassword { get; init; }

    public string? CookiePath { get; init; }
    public string? RpcAuthFilePath { get; init; }

    public long GpuWorkChunkSize { get; init; } = 100_000_000L;
    public int MaxGpuDevices { get; init; } = 2;
    public bool Benchmark { get; init; } = false;

    const string DefaultPayoutScriptHex = "0014b1decf078678a2c716d277b66d0776caef39a214";
    public string PayoutScriptHex { get; init; } = DefaultPayoutScriptHex;

    public static MiningOptions Parse(string[] args)
    {
        MiningOptions options = new();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rpc-url": options = options with { RpcUrl = new Uri(RequireValue(args, ref i)) }; break;
                case "--rpc-user": options = options with { RpcUser = RequireValue(args, ref i) }; break;
                case "--rpc-password": options = options with { RpcPassword = RequireValue(args, ref i) }; break;
                case "--cookie": options = options with { CookiePath = RequireValue(args, ref i) }; break;
                case "--rpc-auth-file": options = options with { RpcAuthFilePath = RequireValue(args, ref i) }; break;
                case "--chunk": options = options with { GpuWorkChunkSize = long.Parse(RequireValue(args, ref i), CultureInfo.InvariantCulture) }; break;
                case "--max-gpus": options = options with { MaxGpuDevices = int.Parse(RequireValue(args, ref i), CultureInfo.InvariantCulture) }; break;
                case "--payout-script": options = options with { PayoutScriptHex = RequireValue(args, ref i) }; break;
                case "--benchmark": options = options with { Benchmark = true }; break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ApplicationException($"Unknown option '{args[i]}'.");
            }
        }

        if (!options.Benchmark)
        {
            if (!string.IsNullOrWhiteSpace(options.CookiePath) && !string.IsNullOrWhiteSpace(options.RpcAuthFilePath))
            {
                throw new ApplicationException("Specify only one of --cookie or --rpc-auth-file.");
            }

            bool hasExplicitAuth = !string.IsNullOrWhiteSpace(options.RpcUser) && !string.IsNullOrWhiteSpace(options.RpcPassword);

            if (string.IsNullOrWhiteSpace(options.CookiePath) && string.IsNullOrWhiteSpace(options.RpcAuthFilePath) && !hasExplicitAuth)
            {
                options = options with { CookiePath = ResolveDefaultCookiePath() };
            }
        }

        return options;
    }

    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length) throw new ApplicationException("Missing value for argument.");
        return args[++index];
    }

    private static string ResolveDefaultCookiePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Bitcoin", ".cookie");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  miner [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --benchmark               Run a 10-second offline hash rate benchmark.");
        Console.WriteLine();
        Console.WriteLine("  --rpc-url <url>           Bitcoin Core RPC URL.");
        Console.WriteLine("                            Default: http://127.0.0.1:8332");
        Console.WriteLine();
        Console.WriteLine("  --rpc-user <user>         RPC username (used with --rpc-password).");
        Console.WriteLine("  --rpc-password <pass>     RPC password (used with --rpc-user).");
        Console.WriteLine();
        Console.WriteLine("  --cookie <path>           Bitcoin Core auth cookie file path.");
        Console.WriteLine("                            File format: __cookie__:randomtoken");
        Console.WriteLine();
        Console.WriteLine("  --rpc-auth-file <path>    RPC credentials file path.");
        Console.WriteLine("                            File format: username:password");
        Console.WriteLine();
        Console.WriteLine("  --chunk <count>           Nonces per GPU dispatch chunk.");
        Console.WriteLine("                            Default: 100,000,000");
        Console.WriteLine();
        Console.WriteLine("  --max-gpus <count>        Maximum number of GPUs to use.");
        Console.WriteLine("                            Default: 2");
        Console.WriteLine();
        Console.WriteLine("  --payout-script <hex>     ScriptPubKey hex for coinbase payout output.");
        Console.WriteLine("                            Default: " + DefaultPayoutScriptHex);
        Console.WriteLine();
        Console.WriteLine("  -h, --help                Show this help text.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  miner --benchmark --max-gpus 2");
        Console.WriteLine("  miner --rpc-url \"http://127.0.0.1:8332\" --rpc-user grok --rpc-password miner --chunk 500000000");
    }
}
