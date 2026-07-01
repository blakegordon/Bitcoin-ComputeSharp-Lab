using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;

[assembly: SupportedOSPlatform("windows")]

namespace Miner.Tests;

public class BlockHeaderAndBlockAssemblyTests
{
    [Fact(DisplayName = nameof(BlockHeader_ToBytes_WritesGenesisHeaderInExpectedWireFormat))]
    public void BlockHeader_ToBytes_WritesGenesisHeaderInExpectedWireFormat()
    {
        // Arrange
        BlockHeader genesisHeader = new(
            Version: 1u,
            PreviousBlockHashBytes: new byte[32],
            MerkleRootBytes: Convert.FromHexString("3BA3EDFD7A7B12B27AC72C3E67768F617FC81BC3888A51323A9FB8AA4B1E5E4A"),
            Time: 1231006505u,
            Bits: 0x1d00ffffu,
            Nonce: 2083236893u,
            FullWitnessCoinbaseBytes: []);

        // Act
        byte[] headerBytes = genesisHeader.ToBytes();
        string actualHex = Convert.ToHexString(headerBytes);

        // Assert
        Assert.Equal(
            "0100000000000000000000000000000000000000000000000000000000000000000000003BA3EDFD7A7B12B27AC72C3E67768F617FC81BC3888A51323A9FB8AA4B1E5E4A29AB5F49FFFF001D1DAC2B7C",
            actualHex);
    }

    [Fact(DisplayName = nameof(BlockHeader_Create_ComputesExpectedMerkleRoot_ForSingleTransactionTemplate))]
    public void BlockHeader_Create_ComputesExpectedMerkleRoot_ForSingleTransactionTemplate()
    {
        // Arrange
        const string payoutScriptHex = "0014b3da2723e05ee70cf62159fd5def1a62b8a0fd8e";
        const string txIdHex = "1111111111111111111111111111111111111111111111111111111111111111";

        GetBlockTemplateResponse template = new()
        {
            Version = 4,
            PreviousBlockHash = "0000000000000000000000000000000000000000000000000000000000000000",
            CurTime = 0x66554433,
            Bits = "1d00ffff",
            Height = 1000,
            CoinbaseValue = 50_0000_0000,
            DefaultWitnessCommitment = "6a24aa21a9ed0000000000000000000000000000000000000000000000000000000000000000",
            Transactions =
            [
                new TransactionTemplate
                {
                    TxId = txIdHex
                }
            ]
        };

        var branches = BlockHeader.PrecomputeMerkleBranches(template);

        // Act
        BlockHeader header = BlockHeader.Create(template, payoutScriptHex, 42u, branches);

        // Assert
        (byte[] coinbaseTxIdBytes, _) = CoinbaseBuilder.Build(
            template.Height,
            template.CoinbaseValue,
            template.DefaultWitnessCommitment,
            payoutScriptHex,
            42u);

        byte[] coinbaseHash = DoubleSha256.Compute(coinbaseTxIdBytes);
        byte[] txHash = Convert.FromHexString(txIdHex);
        Array.Reverse(txHash);

        byte[] combined = new byte[64];
        Buffer.BlockCopy(coinbaseHash, 0, combined, 0, 32);
        Buffer.BlockCopy(txHash, 0, combined, 32, 32);
        byte[] expectedMerkleRoot = DoubleSha256.Compute(combined);

        Assert.Equal(template.Version, header.Version);
        Assert.Equal(template.CurTime, header.Time);
        Assert.Equal(BlockHeader.ParseCompactBits(template.Bits), header.Bits);
        Assert.Equal(expectedMerkleRoot, header.MerkleRootBytes);
        Assert.Equal(0u, header.Nonce);
    }

    [Fact(DisplayName = nameof(BuildFullBlockHex_AppendsHeaderTxCountCoinbaseAndTransactions))]
    public void BuildFullBlockHex_AppendsHeaderTxCountCoinbaseAndTransactions()
    {
        // Arrange
        BlockHeader solvedHeader = new(
            Version: 0x01020304u,
            PreviousBlockHashBytes: [.. Enumerable.Range(0, 32).Select(static i => (byte)i)],
            MerkleRootBytes: [.. Enumerable.Range(32, 32).Select(static i => (byte)i)],
            Time: 0x0A0B0C0Du,
            Bits: 0x1d00ffffu,
            Nonce: 0x11223344u,
            FullWitnessCoinbaseBytes: [0xAA, 0xBB, 0xCC]);

        GetBlockTemplateResponse template = new()
        {
            Transactions =
            [
                new TransactionTemplate { Data = "DEADBEEF" },
                new TransactionTemplate { Data = "0102" }
            ]
        };

        MethodInfo method = typeof(GpuMiningWorker).GetMethod(
            "BuildFullBlockHex",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        string actualHex = (string)method.Invoke(null, [solvedHeader, template])!;

        // Assert
        List<byte> expectedBytes = [.. solvedHeader.ToBytes()];
        expectedBytes.Add(0x03);
        expectedBytes.AddRange([0xAA, 0xBB, 0xCC]);
        expectedBytes.AddRange([0xDE, 0xAD, 0xBE, 0xEF]);
        expectedBytes.AddRange([0x01, 0x02]);

        string expectedHex = Convert.ToHexString([.. expectedBytes]).ToLowerInvariant();

        Assert.Equal(expectedHex, actualHex);
    }

    [Fact(DisplayName = nameof(EndToEnd_BlockBuild_ProducesHeaderCoinbaseMerkleRootAndFullBlockHex_FromRealisticTemplate))]
    public void EndToEnd_BlockBuild_ProducesHeaderCoinbaseMerkleRootAndFullBlockHex_FromRealisticTemplate()
    {
        // Arrange
        const string payoutScriptHex = "0014b3da2723e05ee70cf62159fd5def1a62b8a0fd8e";
        const uint extraNonce = 0xA1B2C3D4;
        const uint solvedNonce = 0x0BADF00D;

        GetBlockTemplateResponse template = new()
        {
            Version = 0x20000000,
            PreviousBlockHash = "00000000000000000000000000000000000000000000000000000000000000ab",
            CurTime = 0x66554433,
            Bits = "1d00ffff",
            Height = 840000,
            CoinbaseValue = 3_125_000_000,
            DefaultWitnessCommitment = "6a24aa21a9ed11223344556677889900AABBCCDDEEFF00112233445566778899AABBCCDD",
            Transactions =
            [
                new TransactionTemplate
                {
                    TxId = "1111111111111111111111111111111111111111111111111111111111111111",
                    Data = "DEADBEEF"
                },
                new TransactionTemplate
                {
                    TxId = "2222222222222222222222222222222222222222222222222222222222222222",
                    Data = "CAFEBABE00"
                }
            ]
        };

        MethodInfo buildFullBlockHexMethod = typeof(GpuMiningWorker).GetMethod(
            "BuildFullBlockHex",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var branches = BlockHeader.PrecomputeMerkleBranches(template);
        BlockHeader unsolvedHeader = BlockHeader.Create(template, payoutScriptHex, extraNonce, branches);
        BlockHeader solvedHeader = unsolvedHeader with { Nonce = solvedNonce };
        byte[] actualHeaderBytes = solvedHeader.ToBytes();
        string actualFullBlockHex = (string)buildFullBlockHexMethod.Invoke(null, [solvedHeader, template])!;

        // Assert
        (byte[] expectedCoinbaseTxIdBytes, byte[] expectedFullWitnessCoinbaseBytes) = CoinbaseBuilder.Build(
            template.Height,
            template.CoinbaseValue,
            template.DefaultWitnessCommitment,
            payoutScriptHex,
            extraNonce);

        byte[] expectedCoinbaseHash = DoubleSha256.Compute(expectedCoinbaseTxIdBytes);
        byte[] expectedMerkleRoot = ComputeMerkleRootFromCoinbaseAndTemplateTransactions(expectedCoinbaseHash, template);

        Assert.Equal(template.Version, solvedHeader.Version);
        Assert.Equal(template.CurTime, solvedHeader.Time);
        Assert.Equal(BlockHeader.ParseCompactBits(template.Bits), solvedHeader.Bits);
        Assert.Equal(solvedNonce, solvedHeader.Nonce);
        Assert.Equal(BlockHeader.ParseBitcoinHash(template.PreviousBlockHash), solvedHeader.PreviousBlockHashBytes);
        Assert.Equal(expectedFullWitnessCoinbaseBytes, solvedHeader.FullWitnessCoinbaseBytes);
        Assert.Equal(expectedMerkleRoot, solvedHeader.MerkleRootBytes);

        byte[] fullBlockBytes = Convert.FromHexString(actualFullBlockHex);
        Assert.Equal(actualHeaderBytes, fullBlockBytes[..80]);
        Assert.Equal(0x03, fullBlockBytes[80]);

        List<byte> expectedFullBlockBytes = [.. actualHeaderBytes, 0x03];
        expectedFullBlockBytes.AddRange(expectedFullWitnessCoinbaseBytes);
        expectedFullBlockBytes.AddRange(Convert.FromHexString(template.Transactions![0].Data!));
        expectedFullBlockBytes.AddRange(Convert.FromHexString(template.Transactions![1].Data!));

        Assert.Equal(expectedFullBlockBytes.ToArray(), fullBlockBytes);
    }

    [Fact(DisplayName = nameof(EndToEnd_BlockBuild_OracleMatchesProductionPipeline_ForRealisticTemplate))]
    public void EndToEnd_BlockBuild_OracleMatchesProductionPipeline_ForRealisticTemplate()
    {
        // Arrange
        const string payoutScriptHex = "0014b3da2723e05ee70cf62159fd5def1a62b8a0fd8e";
        const uint extraNonce = 0x01020304;
        const uint solvedNonce = 0xAABBCCDD;

        GetBlockTemplateResponse template = new()
        {
            Version = 0x20000000,
            PreviousBlockHash = "00000000000000000000000000000000000000000000000000000000000000ab",
            CurTime = 0x66554433,
            Bits = "1d00ffff",
            Height = 840000,
            CoinbaseValue = 3_125_000_000,
            DefaultWitnessCommitment = "6a24aa21a9ed11223344556677889900AABBCCDDEEFF00112233445566778899AABBCCDD",
            Transactions =
            [
                new TransactionTemplate
                {
                    TxId = "1111111111111111111111111111111111111111111111111111111111111111",
                    Data = "DEADBEEF"
                },
                new TransactionTemplate
                {
                    TxId = "2222222222222222222222222222222222222222222222222222222222222222",
                    Data = "CAFEBABE00"
                }
            ]
        };

        MethodInfo buildFullBlockHexMethod = typeof(GpuMiningWorker).GetMethod(
            "BuildFullBlockHex",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var branches = BlockHeader.PrecomputeMerkleBranches(template);
        BlockHeader productionUnsolvedHeader = BlockHeader.Create(template, payoutScriptHex, extraNonce, branches);
        BlockHeader productionSolvedHeader = productionUnsolvedHeader with { Nonce = solvedNonce };
        string actualFullBlockHex = (string)buildFullBlockHexMethod.Invoke(null, [productionSolvedHeader, template])!;

        OracleBlockArtifacts expected = BuildBlockOracle(template, payoutScriptHex, extraNonce, solvedNonce);

        // Assert
        Assert.Equal(expected.HeaderBytes, productionSolvedHeader.ToBytes());
        Assert.Equal(expected.MerkleRootBytes, productionSolvedHeader.MerkleRootBytes);
        Assert.Equal(expected.FullWitnessCoinbaseBytes, productionSolvedHeader.FullWitnessCoinbaseBytes);
        Assert.Equal(expected.FullBlockHex, actualFullBlockHex);
    }

    private sealed record OracleBlockArtifacts(
        byte[] HeaderBytes,
        byte[] MerkleRootBytes,
        byte[] FullWitnessCoinbaseBytes,
        string FullBlockHex);

    private static OracleBlockArtifacts BuildBlockOracle(
        GetBlockTemplateResponse template,
        string payoutScriptHex,
        uint extraNonce,
        uint solvedNonce)
    {
        byte[] previousBlockHashBytes = ParseBitcoinHashOracle(template.PreviousBlockHash);
        uint bits = ParseCompactBitsOracle(template.Bits);

        (byte[] coinbaseTxIdBytes, byte[] fullWitnessCoinbaseBytes) = BuildCoinbaseOracle(
            template.Height,
            template.CoinbaseValue,
            template.DefaultWitnessCommitment!,
            payoutScriptHex,
            extraNonce);

        byte[] coinbaseHash = ComputeDoubleSha256(coinbaseTxIdBytes);
        byte[] merkleRootBytes = ComputeMerkleRootOracle(coinbaseHash, template.Transactions);
        byte[] headerBytes = BuildHeaderOracle(
            template.Version,
            previousBlockHashBytes,
            merkleRootBytes,
            template.CurTime,
            bits,
            solvedNonce);

        List<byte> blockBytes = [.. headerBytes];
        WriteVarInt(blockBytes, 1 + (template.Transactions?.Length ?? 0));
        blockBytes.AddRange(fullWitnessCoinbaseBytes);

        if (template.Transactions is not null)
        {
            foreach (TransactionTemplate tx in template.Transactions)
            {
                if (!string.IsNullOrWhiteSpace(tx.Data))
                {
                    blockBytes.AddRange(Convert.FromHexString(StripHexPrefix(tx.Data)));
                }
            }
        }

        return new OracleBlockArtifacts(
            HeaderBytes: headerBytes,
            MerkleRootBytes: merkleRootBytes,
            FullWitnessCoinbaseBytes: fullWitnessCoinbaseBytes,
            FullBlockHex: Convert.ToHexString([.. blockBytes]).ToLowerInvariant());
    }

    private static (byte[] TxIdBytes, byte[] FullWitnessBytes) BuildCoinbaseOracle(
        long blockHeight,
        long coinbaseValue,
        string defaultWitnessCommitment,
        string payoutScriptHex,
        uint extraNonce)
    {
        List<byte> heightBytes = [];
        long height = blockHeight;

        while (height > 0)
        {
            heightBytes.Add((byte)(height & 0xff));
            height >>= 8;
        }

        if (heightBytes.Count > 0 && (heightBytes[^1] & 0x80) != 0)
        {
            heightBytes.Add(0x00);
        }

        List<byte> scriptSig = [];
        WriteVarInt(scriptSig, heightBytes.Count);
        scriptSig.AddRange(heightBytes);

        byte[] extraNonceBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(extraNonceBytes, extraNonce);
        WriteVarInt(scriptSig, extraNonceBytes.Length);
        scriptSig.AddRange(extraNonceBytes);

        byte[] minerText = CoinbaseBuilder.MinerText;
        WriteVarInt(scriptSig, minerText.Length);
        scriptSig.AddRange(minerText);

        byte[] payoutScript = Convert.FromHexString(StripHexPrefix(payoutScriptHex));
        byte[] witnessCommitment = Convert.FromHexString(StripHexPrefix(defaultWitnessCommitment));

        List<byte> txIdData = [];
        AppendUInt32LittleEndian(txIdData, 1u);
        txIdData.Add(0x01);
        txIdData.AddRange(new byte[32]);
        txIdData.AddRange([0xff, 0xff, 0xff, 0xff]);
        WriteVarInt(txIdData, scriptSig.Count);
        txIdData.AddRange(scriptSig);
        txIdData.AddRange([0xff, 0xff, 0xff, 0xff]);
        txIdData.Add(0x02);
        AppendInt64LittleEndian(txIdData, coinbaseValue);
        WriteVarInt(txIdData, payoutScript.Length);
        txIdData.AddRange(payoutScript);
        AppendInt64LittleEndian(txIdData, 0L);
        WriteVarInt(txIdData, witnessCommitment.Length);
        txIdData.AddRange(witnessCommitment);
        txIdData.AddRange([0x00, 0x00, 0x00, 0x00]);

        List<byte> witnessData = [];
        AppendUInt32LittleEndian(witnessData, 1u);
        witnessData.Add(0x00);
        witnessData.Add(0x01);
        witnessData.Add(0x01);
        witnessData.AddRange(new byte[32]);
        witnessData.AddRange([0xff, 0xff, 0xff, 0xff]);
        WriteVarInt(witnessData, scriptSig.Count);
        witnessData.AddRange(scriptSig);
        witnessData.AddRange([0xff, 0xff, 0xff, 0xff]);
        witnessData.Add(0x02);
        AppendInt64LittleEndian(witnessData, coinbaseValue);
        WriteVarInt(witnessData, payoutScript.Length);
        witnessData.AddRange(payoutScript);
        AppendInt64LittleEndian(witnessData, 0L);
        WriteVarInt(witnessData, witnessCommitment.Length);
        witnessData.AddRange(witnessCommitment);
        witnessData.Add(0x01);
        witnessData.Add(0x20);
        witnessData.AddRange(new byte[32]);
        witnessData.AddRange([0x00, 0x00, 0x00, 0x00]);

        return ([.. txIdData], [.. witnessData]);
    }

    private static byte[] ComputeMerkleRootFromCoinbaseAndTemplateTransactions(byte[] coinbaseHash, GetBlockTemplateResponse template)
    {
        List<byte[]> level = [coinbaseHash];

        if (template.Transactions is not null)
        {
            foreach (TransactionTemplate tx in template.Transactions)
            {
                byte[] txHash = Convert.FromHexString(tx.TxId!);
                Array.Reverse(txHash);
                level.Add(txHash);
            }
        }

        while (level.Count > 1)
        {
            List<byte[]> nextLevel = new((level.Count + 1) / 2);

            for (int i = 0; i < level.Count; i += 2)
            {
                byte[] left = level[i];
                byte[] right = i + 1 < level.Count ? level[i + 1] : left;

                byte[] combined = new byte[64];
                Buffer.BlockCopy(left, 0, combined, 0, 32);
                Buffer.BlockCopy(right, 0, combined, 32, 32);
                nextLevel.Add(DoubleSha256.Compute(combined));
            }

            level = nextLevel;
        }

        return level[0];
    }

    private static byte[] ComputeMerkleRootOracle(byte[] coinbaseHash, IReadOnlyList<TransactionTemplate>? transactions)
    {
        List<byte[]> level = [coinbaseHash];

        if (transactions is not null)
        {
            foreach (TransactionTemplate tx in transactions)
            {
                byte[] txHashBytes = Convert.FromHexString(StripHexPrefix(tx.TxId!));
                Array.Reverse(txHashBytes);
                level.Add(txHashBytes);
            }
        }

        while (level.Count > 1)
        {
            List<byte[]> nextLevel = new((level.Count + 1) / 2);

            for (int i = 0; i < level.Count; i += 2)
            {
                byte[] left = level[i];
                byte[] right = i + 1 < level.Count ? level[i + 1] : left;

                byte[] combined = new byte[64];
                Buffer.BlockCopy(left, 0, combined, 0, 32);
                Buffer.BlockCopy(right, 0, combined, 32, 32);
                nextLevel.Add(ComputeDoubleSha256(combined));
            }

            level = nextLevel;
        }

        return level[0];
    }

    private static byte[] BuildHeaderOracle(
        uint version,
        byte[] previousBlockHashBytes,
        byte[] merkleRootBytes,
        uint time,
        uint bits,
        uint nonce)
    {
        byte[] headerBytes = new byte[80];

        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(0, 4), version);
        Buffer.BlockCopy(previousBlockHashBytes, 0, headerBytes, 4, 32);
        Buffer.BlockCopy(merkleRootBytes, 0, headerBytes, 36, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(68, 4), time);
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(72, 4), bits);
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(76, 4), nonce);

        return headerBytes;
    }

    private static byte[] ParseBitcoinHashOracle(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return new byte[32];
        }

        byte[] bytes = Convert.FromHexString(StripHexPrefix(hex));

        if (bytes.Length > 32)
        {
            bytes = bytes[..32];
        }

        if (bytes.Length < 32)
        {
            Array.Resize(ref bytes, 32);
        }

        Array.Reverse(bytes);
        return bytes;
    }

    private static uint ParseCompactBitsOracle(string? bits)
    {
        return uint.Parse(StripHexPrefix(bits ?? throw new InvalidOperationException("Missing bits.")), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static byte[] ComputeDoubleSha256(ReadOnlySpan<byte> data)
    {
        Span<byte> first = stackalloc byte[32];
        SHA256.HashData(data, first);

        byte[] second = new byte[32];
        SHA256.HashData(first, second);
        return second;
    }

    private static void WriteVarInt(List<byte> bytes, long value)
    {
        if (value < 0xfd)
        {
            bytes.Add((byte)value);
        }
        else if (value <= 0xffff)
        {
            bytes.Add(0xfd);
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
        }
        else if (value <= 0xffffffff)
        {
            bytes.Add(0xfe);
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 24));
        }
        else
        {
            bytes.Add(0xff);
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 32));
            bytes.Add((byte)(value >> 40));
            bytes.Add((byte)(value >> 48));
            bytes.Add((byte)(value >> 56));
        }
    }

    private static void AppendUInt32LittleEndian(List<byte> bytes, uint value)
    {
        byte[] buffer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static void AppendInt64LittleEndian(List<byte> bytes, long value)
    {
        byte[] buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static string StripHexPrefix(string value)
    {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
    }
}
