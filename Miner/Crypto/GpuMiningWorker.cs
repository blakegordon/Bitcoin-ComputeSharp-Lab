using ComputeSharp;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;

[SuppressMessage("Design", "CA1031:Do not catch general exception types")]
internal sealed class GpuMiningWorker(BitcoinRpcClient rpc, MiningOptions options, GraphicsDevice device, int gpuIndex)
{
    private const int MaxTemplateAgeSeconds = 15;
    private readonly BitcoinRpcClient _rpc = rpc;
    private readonly MiningOptions _options = options;
    private readonly int _gpuIndex = gpuIndex;
    private readonly string _preferredDeviceName = device.Name;

    private GraphicsDevice _device = device;

    private long _totalNonces = 0;
    private DateTime _lastReport = DateTime.UtcNow;

    private static readonly ConcurrentDictionary<int, string> _labels = new();
    private static readonly ConcurrentDictionary<int, double> _rates = new();
    private static DateTime _lastCombinedReport = DateTime.UtcNow;
    private static readonly Lock _rateLock = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string shortName = GetShortName(_device.Name);
        Console.WriteLine();
        _labels[_gpuIndex] = shortName;

        uint[] zeroFlag = [0, 0];
        uint[] gpuResult = new uint[2];

        uint extraNonce = (uint)_gpuIndex;

        ReadWriteBuffer<uint> resultBuffer = _device.AllocateReadWriteBuffer<uint>(2);
        resultBuffer.CopyFrom(zeroFlag);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    GetBlockTemplateResponse template = await _rpc.GetBlockTemplateAsync(cancellationToken).ConfigureAwait(false);
                    DateTime templateFetchTime = DateTime.UtcNow;

                    uint bits = BlockHeader.ParseCompactBits(template.Bits);
                    uint[] t = BuildTargetWords(bits);

                    byte[][] merkleBranches = BlockHeader.PrecomputeMerkleBranches(template);

                    bool blockFound = false;

                    while (!blockFound && !cancellationToken.IsCancellationRequested && (DateTime.UtcNow - templateFetchTime).TotalSeconds < MaxTemplateAgeSeconds)
                    {
                        BlockHeader headerTemplate = BlockHeader.Create(template, _options.PayoutScriptHex, extraNonce, [.. merkleBranches]);

                        byte[] headerBytes = headerTemplate.ToBytes();
                        byte[] first64 = new byte[64];
                        Buffer.BlockCopy(headerBytes, 0, first64, 0, 64);

                        uint[] ms = MidstateCalculator.ComputeMidstate(first64);
                        uint m0 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(64, 4));
                        uint m2 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(72, 4));

                        int maxTimeDrift = 128;
                        for (int roll = 0; roll < maxTimeDrift && !blockFound && !cancellationToken.IsCancellationRequested && (DateTime.UtcNow - templateFetchTime).TotalSeconds < MaxTemplateAgeSeconds; roll++)
                        {
                            uint currentTimeNative = ReadTimeNative(headerBytes) + (uint)roll;
                            uint m1 = SwapTimeForShader(currentTimeNative);

                            long currentNonce = 0;

                            while (currentNonce < 0x100000000L && !blockFound && !cancellationToken.IsCancellationRequested)
                            {
                                long candidateCount = Math.Min((long)_options.GpuWorkChunkSize, 0x100000000L - currentNonce);
                                if (candidateCount <= 0) break;

                                bool chunkHit = ExecuteShaderChunk(_device, ms, m0, m1, m2, t, currentNonce, candidateCount, resultBuffer, gpuResult);

                                _totalNonces += candidateCount;
                                currentNonce += candidateCount;

                                if ((DateTime.UtcNow - _lastReport).TotalSeconds > 5)
                                {
                                    double elapsed = (DateTime.UtcNow - _lastReport).TotalSeconds;
                                    double ghps = _totalNonces / Math.Max(elapsed, 1e-6) / 1_000_000_000.0;

                                    lock (_rateLock)
                                    {
                                        _rates[_gpuIndex] = ghps;

                                        // Wait until all registered GPUs have reported a rate before drawing the console line
                                        if (_rates.Count == _labels.Count && (DateTime.UtcNow - _lastCombinedReport).TotalSeconds > 5)
                                        {
                                            var parts = new List<string>();
                                            foreach (var kv in _labels.OrderBy(k => k.Key))
                                            {
                                                double rate = _rates.TryGetValue(kv.Key, out var r) ? r : 0.0;
                                                parts.Add($"{kv.Value}: {rate,6:F2} GH/s");
                                            }
                                            Console.WriteLine(string.Join("         ", parts));
                                            _lastCombinedReport = DateTime.UtcNow;
                                        }
                                    }

                                    _totalNonces = 0;
                                    _lastReport = DateTime.UtcNow;
                                }

                                if (chunkHit)
                                {
                                    uint nonce = gpuResult[1];
                                    resultBuffer.CopyFrom(zeroFlag);

                                    BlockHeader solved = headerTemplate with { Nonce = nonce, Time = currentTimeNative };
                                    byte[] hash = DoubleSha256.Compute(solved.ToBytes());

                                    if (MeetsTarget(hash, solved.Bits))
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine($"\n{_device.Name} found solution with nonce {nonce}!");
                                        string fullBlockHex = BuildFullBlockHex(solved, template);
                                        string? submitResult = await _rpc.SubmitBlockAsync(fullBlockHex, cancellationToken).ConfigureAwait(false);

                                        string blockHash = ComputeBlockHash(solved);
                                        LogBlockFound(_device.Name, nonce, template, blockHash, fullBlockHex, submitResult);

                                        if (string.IsNullOrWhiteSpace(submitResult) || submitResult.Equals("null", StringComparison.OrdinalIgnoreCase))
                                        {
                                            PersistentBlockFoundAlert(_device.Name, nonce, blockHash);
                                            while (!cancellationToken.IsCancellationRequested)
                                            {
                                                TryBeep();
                                                await Task.Delay(10000, cancellationToken).ConfigureAwait(false);
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine($"submitblock: {submitResult}");
                                        }

                                        blockFound = true;
                                    }
                                }
                            }
                        }

                        extraNonce += (uint)_options.MaxGpuDevices;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (IsDeviceLostException(ex))
                {
                    Console.Error.WriteLine($"{_device.Name} worker warning: GPU device lost/suspended. Attempting recovery...");

                    if (TryRecoverDevice(ref resultBuffer, zeroFlag))
                    {
                        _totalNonces = 0;
                        _lastReport = DateTime.UtcNow;
                        Console.WriteLine($"{_device.Name} worker recovered and resumed.");
                    }
                    else
                    {
                        Console.Error.WriteLine($"{_device.Name} worker error: GPU recovery failed.");
                        await Task.Delay(2_000, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.Error.WriteLine($"{_device.Name} network error: {ex.Message}");
                    await Task.Delay(2_000, cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"{_device.Name} RPC parsing error: {ex.Message}");
                    await Task.Delay(2_000, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"{_device.Name} RPC/protocol error: {ex.Message}");
                    await Task.Delay(2_000, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            TryDispose(resultBuffer);
            TryDispose(_device as IDisposable);
            Console.WriteLine($"{_device.Name} worker stopping.");
        }
    }

    public Task RunBenchmarkAsync(CancellationToken cancellationToken)
    {
        string shortName = GetShortName(_device.Name);
        Console.WriteLine($"  {shortName} warming up...");

        uint[] zeroFlag = [0, 0];
        uint[] gpuResult = new uint[2];

        ReadWriteBuffer<uint> resultBuffer = _device.AllocateReadWriteBuffer<uint>(2);
        resultBuffer.CopyFrom(zeroFlag);

        // Dummy 80-byte header with extremely high difficulty (Mainnet equivalent)
        byte[] headerBytes = new byte[80];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(0, 4), 0x20000000); // Version
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(72, 4), 0x170248a3); // Bits (~133.87T difficulty)

        byte[] first64 = new byte[64];
        Buffer.BlockCopy(headerBytes, 0, first64, 0, 64);

        uint[] ms = MidstateCalculator.ComputeMidstate(first64);
        uint m0 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(64, 4));
        uint m1 = SwapTimeForShader(ReadTimeNative(headerBytes));
        uint m2 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(72, 4));

        uint[] t = BuildTargetWords(0x170248a3);

        // Stagger starting nonce per GPU
        long currentNonce = (long)_gpuIndex * 0x10000000L;
        long totalHashes = 0;

        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            // Run exactly 10 seconds per GPU
            while (sw.Elapsed.TotalSeconds < 10 && !cancellationToken.IsCancellationRequested)
            {
                long candidateCount = _options.GpuWorkChunkSize;

                bool chunkHit = ExecuteShaderChunk(_device, ms, m0, m1, m2, t, currentNonce, candidateCount, resultBuffer, gpuResult);

                totalHashes += candidateCount;
                currentNonce += candidateCount;

                if (chunkHit)
                {
                    // In the absurdly rare case it actually finds a mainnet-difficulty block, reset the flag and keep going
                    resultBuffer.CopyFrom(zeroFlag);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            sw.Stop();
            TryDispose(resultBuffer);
            TryDispose(_device as IDisposable);

            double seconds = sw.Elapsed.TotalSeconds;
            double ghps = totalHashes / Math.Max(seconds, 1e-6) / 1_000_000_000.0;

            Console.WriteLine();
            Console.WriteLine($"[BENCHMARK] {shortName}: {ghps:F2} GH/s ({totalHashes:N0} hashes in {seconds:F2}s)");
        }

        return Task.CompletedTask;
    }

    private static string GetShortName(string fullName) => fullName.Replace("NVIDIA GeForce ", "").Replace("NVIDIA ", "").Replace("GeForce ", "");

    private static bool IsDeviceLostException(Exception ex)
    {
        string message = ex.Message ?? string.Empty;
        return message.Contains("device instance has been suspended", StringComparison.OrdinalIgnoreCase)
            || message.Contains("has been lost", StringComparison.OrdinalIgnoreCase)
            || message.Contains("device removed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("device reset", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryRecoverDevice(ref ReadWriteBuffer<uint> resultBuffer, uint[] zeroFlag)
    {
        try
        {
            GraphicsDevice[] devices = [.. GraphicsDevice.QueryDevices(static d => d.IsHardwareAccelerated)];
            if (devices.Length == 0) return false;

            GraphicsDevice replacement =
                devices.FirstOrDefault(d => string.Equals(d.Name, _preferredDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? devices[Math.Clamp(_gpuIndex, 0, devices.Length - 1)];

            TryDispose(resultBuffer);
            if (!ReferenceEquals(_device, replacement))
            {
                TryDispose(_device as IDisposable);
            }

            _device = replacement;
            resultBuffer = _device.AllocateReadWriteBuffer<uint>(2);
            resultBuffer.CopyFrom(zeroFlag);

            _labels[_gpuIndex] = GetShortName(_device.Name);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to recover device state: {ex.Message}");
            return false;
        }
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try { disposable?.Dispose(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Resource disposal error: {ex.Message}");
        }
    }

    internal static bool ExecuteShaderChunk(GraphicsDevice device, uint[] ms, uint m0, uint m1, uint m2, uint[] t, long currentNonce, long candidateCount, ReadWriteBuffer<uint> resultBuffer, uint[] gpuResult)
    {
        int threadsX = 1048576;
        int threadsY = (int)((candidateCount + threadsX - 1) / threadsX);
        int actualThreadsX = threadsY == 1 ? (int)candidateCount : threadsX;

        Debug.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + GetShortName(device.Name) + ": chunk started.");

        device.For(actualThreadsX, threadsY, new HeaderNonceHashShader(
            ms[0], ms[1], ms[2], ms[3], ms[4], ms[5], ms[6], ms[7],
            m0, m1, m2, (uint)currentNonce, (uint)candidateCount, (uint)actualThreadsX,
            t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7],
            resultBuffer));

        Debug.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + GetShortName(device.Name) + ": chunk finished.");

        Debug.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + GetShortName(device.Name) + ": buffer copy from device started.");
        resultBuffer.CopyTo(gpuResult);
        Debug.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + GetShortName(device.Name) + ": buffer copy from device finished.");

        return gpuResult[0] == 1;
    }

    private static string BuildFullBlockHex(BlockHeader solvedHeader, GetBlockTemplateResponse template)
    {
        var block = new List<byte>(solvedHeader.ToBytes());

        int txCount = 1 + (template.Transactions?.Length ?? 0);
        block.AddRange(EncodeVarInt(txCount));

        block.AddRange(solvedHeader.FullWitnessCoinbaseBytes);

        if (template.Transactions != null)
        {
            foreach (var tx in template.Transactions)
            {
                if (!string.IsNullOrWhiteSpace(tx.Data))
                {
                    ReadOnlySpan<char> span = tx.Data.AsSpan();
                    if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) span = span[2..];
                    block.AddRange(Convert.FromHexString(span));
                }
            }
        }

        return Convert.ToHexString([.. block]).ToLowerInvariant();
    }

    private static bool MeetsTarget(byte[] hash, uint bits)
    {
        BigInteger target = DecodeCompactBits(bits);
        BigInteger hashInt = new(hash, isUnsigned: true, isBigEndian: false);
        return hashInt.CompareTo(target) <= 0;
    }

    internal static BigInteger DecodeCompactBits(uint bits)
    {
        int size = (int)((bits >> 24) & 0xFF);
        uint mantissa = bits & 0x007FFFFFu;

        if (size <= 3)
        {
            return new BigInteger(mantissa) >> (8 * (3 - size));
        }

        return new BigInteger(mantissa) << (8 * (size - 3));
    }

    private static void TryBeep()
    {
        if (OperatingSystem.IsWindows())
        {
            Console.Beep(2000, 250);
            Console.Beep(2600, 250);
            Console.Beep(3200, 500);
        }
    }

    internal static uint[] BuildTargetWords(uint bits)
    {
        BigInteger target = DecodeCompactBits(bits);
        byte[] targetBytes = target.ToByteArray(isUnsigned: true, isBigEndian: true);

        byte[] full = new byte[32];
        int start = 32 - targetBytes.Length;
        if (start < 0)
        {
            Buffer.BlockCopy(targetBytes, -start, full, 0, 32);
        }
        else
        {
            Buffer.BlockCopy(targetBytes, 0, full, start, targetBytes.Length);
        }

        uint[] words = new uint[8];
        for (int i = 0; i < 8; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32BigEndian(full.AsSpan(i * 4, 4));
        }

        return words;
    }

    internal static uint ReadTimeNative(byte[] headerBytes)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.AsSpan(68, 4));
    }

    internal static uint SwapTimeForShader(uint nativeTime)
    {
        return BinaryPrimitives.ReverseEndianness(nativeTime);
    }

    private static byte[] EncodeVarInt(long value)
    {
        if (value < 0xfd)
            return [(byte)value];
        if (value <= 0xffff)
            return [0xfd, (byte)value, (byte)(value >> 8)];
        if (value <= 0xffffffff)
            return [0xfe, (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];
        return [ 0xff,
            (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24),
            (byte)(value >> 32), (byte)(value >> 40), (byte)(value >> 48), (byte)(value >> 56) ];
    }

    private static string ComputeBlockHash(BlockHeader header)
    {
        byte[] headerBytes = header.ToBytes();
        byte[] hash = DoubleSha256.Compute(headerBytes);
        Array.Reverse(hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void LogBlockFound(string gpuName, uint nonce, GetBlockTemplateResponse template, string blockHash, string fullBlockHex, string? submitResult)
    {
        try
        {
            string logFile = "blocks_found.log";
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            string submitNote = string.IsNullOrWhiteSpace(submitResult) || submitResult.Equals("null", StringComparison.OrdinalIgnoreCase)
                ? "submitblock accepted (null)"
                : $"submitblock result: {submitResult}";

            string entry = $"""
            ================================================
            BLOCK FOUND!
            Time: {timestamp}
            GPU: {gpuName}
            Nonce: {nonce} (0x{nonce:X8})
            Height: {template.Height}
            Block Hash: {blockHash}
            Full block hex (for manual submitblock): {fullBlockHex}
            {submitNote}
            ================================================
            """;
            File.AppendAllText(logFile, entry + Environment.NewLine + Environment.NewLine);
            Console.WriteLine($"\n>>> Block details logged to {logFile} <<<");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PathTooLongException)
        {
            Console.Error.WriteLine($"Failed to log block find: {ex}");
        }
    }

    private static void PersistentBlockFoundAlert(string gpuName, uint nonce, string blockHash)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("====================================================================");
        Console.WriteLine("                        BLOCK FOUND !!!!");
        Console.WriteLine("====================================================================");
        Console.WriteLine($"GPU: {gpuName}");
        Console.WriteLine($"Nonce: {nonce}");
        Console.WriteLine($"Block Hash: {blockHash}");
        Console.WriteLine("Check 'blocks_found.log' for permanent record.");
        Console.WriteLine("This message will keep repeating until stopped.");
        Console.WriteLine("====================================================================");
        Console.WriteLine();
        Console.ResetColor();
        TryBeep();
    }
}
