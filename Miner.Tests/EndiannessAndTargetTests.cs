using System.Buffers.Binary;
using System.Numerics;

namespace Miner.Tests;

public class EndiannessAndTargetTests
{
    [Fact(DisplayName = nameof(ByteSwap_Roundtrips_Correctly))]
    public void ByteSwap_Roundtrips_Correctly()
    {
        // Arrange
        uint original = 0x12345678;

        // Act
        uint swappedOnce = HeaderNonceHashShader.ByteSwap(original);
        uint swappedTwice = HeaderNonceHashShader.ByteSwap(swappedOnce);

        // Assert
        Assert.Equal(original, swappedTwice);
        Assert.Equal(0x78563412u, swappedOnce);
    }

    [Fact(DisplayName = nameof(Time_Parsing_And_Swapping_For_Shader_Uses_LittleEndian_Native))]
    public void Time_Parsing_And_Swapping_For_Shader_Uses_LittleEndian_Native()
    {
        // Arrange
        byte[] headerBytes = new byte[80];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(68, 4), 0x12345678);

        // Act
        uint nativeTime = GpuMiningWorker.ReadTimeNative(headerBytes);
        uint forShader = GpuMiningWorker.SwapTimeForShader(nativeTime);

        // Assert
        Assert.Equal(0x12345678u, nativeTime);
        Assert.Equal(0x78563412u, forShader);
    }

    [Fact(DisplayName = nameof(MeetsTarget_Uses_LittleEndian_Interpretation_For_Hash))]
    public void MeetsTarget_Uses_LittleEndian_Interpretation_For_Hash()
    {
        // Arrange
        byte[] zeroHash = new byte[32];
        uint easyBits = 0x1d00ffff; // a very easy (high) target

        // Act
        BigInteger target = GpuMiningWorker.DecodeCompactBits(easyBits);

        // Assert
        BigInteger hashInt = new(zeroHash, isUnsigned: true, isBigEndian: false);
        Assert.True(hashInt.CompareTo(target) <= 0);
    }

    [Fact(DisplayName = nameof(DecodeCompactBits_KnownVectors_MapToExpectedTargets))]
    public void DecodeCompactBits_KnownVectors_MapToExpectedTargets()
    {
        // Arrange
        (uint Bits, string ExpectedTargetHex)[] cases =
        [
            (0x1d00ffffu, "00000000FFFF0000000000000000000000000000000000000000000000000000"),
            (0x1f00ffffu, "0000FFFF00000000000000000000000000000000000000000000000000000000"),
            (0x02123456u, "0000000000000000000000000000000000000000000000000000000000001234"),
            (0x03000000u, "0000000000000000000000000000000000000000000000000000000000000000")
        ];

        // Act
        BigInteger[] actualTargets = [.. cases.Select(static c => GpuMiningWorker.DecodeCompactBits(c.Bits))];

        // Assert
        for (int i = 0; i < cases.Length; i++)
        {
            BigInteger expected = ParseUnsignedBigEndianHex(cases[i].ExpectedTargetHex);
            Assert.Equal(expected, actualTargets[i]);
        }
    }

    [Fact(DisplayName = nameof(BuildTargetWords_KnownVectors_MapToExpectedWords))]
    public void BuildTargetWords_KnownVectors_MapToExpectedWords()
    {
        // Arrange
        (uint Bits, uint[] ExpectedWords)[] cases =
        [
            (0x1d00ffffu, [0x00000000u, 0xFFFF0000u, 0u, 0u, 0u, 0u, 0u, 0u]),
            (0x1f00ffffu, [0x0000FFFFu, 0u, 0u, 0u, 0u, 0u, 0u, 0u]),
            (0x02123456u, [0u, 0u, 0u, 0u, 0u, 0u, 0u, 0x00001234u]),
            (0x03000000u, [0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u])
        ];

        // Act
        uint[][] actualWords = [.. cases.Select(static c => GpuMiningWorker.BuildTargetWords(c.Bits))];

        // Assert
        for (int i = 0; i < cases.Length; i++)
        {
            Assert.Equal(cases[i].ExpectedWords, actualWords[i]);
        }
    }

    [Fact(DisplayName = nameof(MidstateCalculator_GenesisHeader_Matches_IndependentReferenceImplementation))]
    public void MidstateCalculator_GenesisHeader_Matches_IndependentReferenceImplementation()
    {
        // Arrange
        byte[] genesisHeader = Convert.FromHexString(
            "0100000000000000000000000000000000000000000000000000000000000000" +
            "000000003BA3EDFD7A7B12B27AC72C3E67768F617FC81BC3888A51323A9FB8AA" +
            "4B1E5E4A29AB5F49FFFF001D1DAC2B7C");

        // Act
        uint[] actualFrom64 = MidstateCalculator.ComputeMidstate(genesisHeader[..64]);
        uint[] actualFrom80 = MidstateCalculator.ComputeMidstateFromHeader(genesisHeader);
        uint[] expected = ComputeReferenceMidstate(genesisHeader[..64]);

        // Assert
        Assert.Equal(expected, actualFrom64);
        Assert.Equal(expected, actualFrom80);
    }

    private static BigInteger ParseUnsignedBigEndianHex(string hex)
    {
        string normalized = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if ((normalized.Length & 1) != 0)
        {
            normalized = "0" + normalized;
        }

        byte[] bytes = Convert.FromHexString(normalized);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    private static uint[] ComputeReferenceMidstate(byte[] first64Bytes)
    {
        uint[] schedule = new uint[64];
        for (int i = 0; i < 16; i++)
        {
            schedule[i] = BinaryPrimitives.ReadUInt32BigEndian(first64Bytes.AsSpan(i * 4, 4));
        }

        for (int i = 16; i < 64; i++)
        {
            uint s0 = RotateRight(schedule[i - 15], 7) ^ RotateRight(schedule[i - 15], 18) ^ (schedule[i - 15] >> 3);
            uint s1 = RotateRight(schedule[i - 2], 17) ^ RotateRight(schedule[i - 2], 19) ^ (schedule[i - 2] >> 10);
            schedule[i] = schedule[i - 16] + s0 + schedule[i - 7] + s1;
        }

        uint a = 0x6A09E667u;
        uint b = 0xBB67AE85u;
        uint c = 0x3C6EF372u;
        uint d = 0xA54FF53Au;
        uint e = 0x510E527Fu;
        uint f = 0x9B05688Cu;
        uint g = 0x1F83D9ABu;
        uint h = 0x5BE0CD19u;

        uint[] k =
        [
            0x428A2F98u, 0x71374491u, 0xB5C0FBCFu, 0xE9B5DBA5u, 0x3956C25Bu, 0x59F111F1u, 0x923F82A4u, 0xAB1C5ED5u,
            0xD807AA98u, 0x12835B01u, 0x243185BEu, 0x550C7DC3u, 0x72BE5D74u, 0x80DEB1FEu, 0x9BDC06A7u, 0xC19BF174u,
            0xE49B69C1u, 0xEFBE4786u, 0x0FC19DC6u, 0x240CA1CCu, 0x2DE92C6Fu, 0x4A7484AAu, 0x5CB0A9DCu, 0x76F988DAu,
            0x983E5152u, 0xA831C66Du, 0xB00327C8u, 0xBF597FC7u, 0xC6E00BF3u, 0xD5A79147u, 0x06CA6351u, 0x14292967u,
            0x27B70A85u, 0x2E1B2138u, 0x4D2C6DFCu, 0x53380D13u, 0x650A7354u, 0x766A0ABBu, 0x81C2C92Eu, 0x92722C85u,
            0xA2BFE8A1u, 0xA81A664Bu, 0xC24B8B70u, 0xC76C51A3u, 0xD192E819u, 0xD6990624u, 0xF40E3585u, 0x106AA070u,
            0x19A4C116u, 0x1E376C08u, 0x2748774Cu, 0x34B0BCB5u, 0x391C0CB3u, 0x4ED8AA4Au, 0x5B9CCA4Fu, 0x682E6FF3u,
            0x748F82EEu, 0x78A5636Fu, 0x84C87814u, 0x8CC70208u, 0x90BEFFFAu, 0xA4506CEBu, 0xBEF9A3F7u, 0xC67178F2u
        ];

        uint initialA = a;
        uint initialB = b;
        uint initialC = c;
        uint initialD = d;
        uint initialE = e;
        uint initialF = f;
        uint initialG = g;
        uint initialH = h;

        for (int i = 0; i < 64; i++)
        {
            uint bigSigma1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
            uint choose = (e & f) ^ (~e & g);
            uint temp1 = h + bigSigma1 + choose + k[i] + schedule[i];

            uint bigSigma0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
            uint majority = (a & b) ^ (a & c) ^ (b & c);
            uint temp2 = bigSigma0 + majority;

            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        return
        [
            initialA + a,
            initialB + b,
            initialC + c,
            initialD + d,
            initialE + e,
            initialF + f,
            initialG + g,
            initialH + h
        ];
    }

    private static uint RotateRight(uint value, int amount) => (value >> amount) | (value << (32 - amount));
}
