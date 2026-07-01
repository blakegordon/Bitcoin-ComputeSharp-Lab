using ComputeSharp;
using System.Buffers.Binary;
using System.Numerics;

namespace Miner.Tests;

public class GpuIntegrationTests
{
    [InlineData(0L)]
    [InlineData(0xFFF00000L)]
    [Theory(DisplayName = nameof(Application_GpuDispatch_Matches_CpuSha256))]
    public void Application_GpuDispatch_Matches_CpuSha256(long nonceSearchStart)
    {
        // Arrange
        byte[] headerBytes = new byte[80];
        Random.Shared.NextBytes(headerBytes.AsSpan(0, 76));

        uint testBits = 0x1f00ffff;
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(72, 4), testBits);

        BigInteger targetInt = GpuMiningWorker.DecodeCompactBits(testBits);
        uint expectedNonce = 0;
        bool foundOnCpu = false;

        for (uint offset = 0; offset < 1_000_000; offset++)
        {
            uint nonce = (uint)nonceSearchStart + offset;
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(76, 4), nonce);
            byte[] cpuHash = DoubleSha256.Compute(headerBytes);
            if (new BigInteger(cpuHash, isUnsigned: true, isBigEndian: false) <= targetInt)
            {
                expectedNonce = nonce;
                foundOnCpu = true;
                break;
            }
        }

        Assert.True(foundOnCpu, "CPU failed to find a valid test nonce within bounds. Try re-running the test.");

        uint[] t = GpuMiningWorker.BuildTargetWords(testBits);
        uint[] ms = MidstateCalculator.ComputeMidstate(headerBytes[..64]);
        uint m0 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(64, 4));
        uint m2 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(72, 4));
        uint m1 = GpuMiningWorker.SwapTimeForShader(GpuMiningWorker.ReadTimeNative(headerBytes));

        long candidateCount = Math.Max(100_000, (long)(expectedNonce - (uint)nonceSearchStart) + 1024);

        using GraphicsDevice device = GraphicsDevice.QueryDevices(static d => d.IsHardwareAccelerated).First();
        using ReadWriteBuffer<uint> resultBuffer = device.AllocateReadWriteBuffer<uint>(2);
        resultBuffer.CopyFrom([0u, 0u]);
        uint[] gpuResult = new uint[2];

        // Act
        bool gpuFound = GpuMiningWorker.ExecuteShaderChunk(
            device, ms, m0, m1, m2, t,
            nonceSearchStart, candidateCount, resultBuffer, gpuResult);

        // Assert
        Assert.True(gpuFound, "The GPU dispatch failed to flag success for a known winning chunk.");

        uint returnedNonce = gpuResult[1];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(76, 4), returnedNonce);
        byte[] gpuValidationHash = DoubleSha256.Compute(headerBytes);
        BigInteger gpuValidationHashVal = new(gpuValidationHash, isUnsigned: true, isBigEndian: false);

        Assert.True(gpuValidationHashVal <= targetInt,
            $"GPU returned a corrupted nonce ({returnedNonce}) that failed CPU verification.");
    } 

    [Fact(DisplayName = nameof(ExecuteShaderChunk_FindsGenesisNonce))]
    public void ExecuteShaderChunk_FindsGenesisNonce()
    {
        // Arrange
        byte[] genesisHeader = Convert.FromHexString(
            "0100000000000000000000000000000000000000000000000000000000000000" +
            "000000003BA3EDFD7A7B12B27AC72C3E67768F617FC81BC3888A51323A9FB8AA" +
            "4B1E5E4A29AB5F49FFFF001D1DAC2B7C");

        // Bitcoin genesis block nonce: 2083236893 (0x7C2BAC1D, stored little-endian at bytes 76-79).
        const uint knownNonce = 2083236893u;

        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(genesisHeader.AsSpan(72, 4));
        uint[] t = GpuMiningWorker.BuildTargetWords(bits);
        uint[] ms = MidstateCalculator.ComputeMidstate(genesisHeader[..64]);
        uint m0 = BinaryPrimitives.ReadUInt32BigEndian(genesisHeader.AsSpan(64, 4));
        uint m2 = BinaryPrimitives.ReadUInt32BigEndian(genesisHeader.AsSpan(72, 4));
        uint m1 = GpuMiningWorker.SwapTimeForShader(GpuMiningWorker.ReadTimeNative(genesisHeader));

        using GraphicsDevice device = GraphicsDevice.QueryDevices(static d => d.IsHardwareAccelerated).First();
        using ReadWriteBuffer<uint> resultBuffer = device.AllocateReadWriteBuffer<uint>(2);
        resultBuffer.CopyFrom([0u, 0u]);
        uint[] gpuResult = new uint[2];

        // Act — search a tiny window of 20 nonces centred on the known answer.
        bool found = GpuMiningWorker.ExecuteShaderChunk(
            device, ms, m0, m1, m2, t,
            knownNonce - 10, 20, resultBuffer, gpuResult);

        // Assert
        Assert.True(found, "GPU did not report a hit for the genesis block's known nonce.");
        Assert.Equal(knownNonce, gpuResult[1]);
    }

    [Fact(DisplayName = nameof(ExecuteShaderChunk_FindsGenesisNonce_WhenDispatchSpansMultipleRows))]
    public void ExecuteShaderChunk_FindsGenesisNonce_WhenDispatchSpansMultipleRows()
    {
        // Arrange
        byte[] genesisHeader = Convert.FromHexString(
            "0100000000000000000000000000000000000000000000000000000000000000" +
            "000000003BA3EDFD7A7B12B27AC72C3E67768F617FC81BC3888A51323A9FB8AA" +
            "4B1E5E4A29AB5F49FFFF001D1DAC2B7C");

        const uint knownNonce = 2083236893u;
        const long candidateCount = 1_048_600;
        uint nonceSearchStart = knownNonce - 1_048_580u;

        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(genesisHeader.AsSpan(72, 4));
        uint[] t = GpuMiningWorker.BuildTargetWords(bits);
        uint[] ms = MidstateCalculator.ComputeMidstate(genesisHeader[..64]);
        uint m0 = BinaryPrimitives.ReadUInt32BigEndian(genesisHeader.AsSpan(64, 4));
        uint m2 = BinaryPrimitives.ReadUInt32BigEndian(genesisHeader.AsSpan(72, 4));
        uint m1 = GpuMiningWorker.SwapTimeForShader(GpuMiningWorker.ReadTimeNative(genesisHeader));

        using GraphicsDevice device = GraphicsDevice.QueryDevices(static d => d.IsHardwareAccelerated).First();
        using ReadWriteBuffer<uint> resultBuffer = device.AllocateReadWriteBuffer<uint>(2);
        resultBuffer.CopyFrom([0u, 0u]);
        uint[] gpuResult = new uint[2];

        // Act
        bool found = GpuMiningWorker.ExecuteShaderChunk(
            device, ms, m0, m1, m2, t,
            nonceSearchStart, candidateCount, resultBuffer, gpuResult);

        // Assert
        Assert.True(found, "GPU did not report a hit when the winning nonce fell into a later dispatch row.");
        Assert.Equal(knownNonce, gpuResult[1]);
    }

    [Fact(DisplayName = nameof(ExecuteShaderChunk_ReturnsNoHit_WhenNoNonceMeetsTarget))]
    public void ExecuteShaderChunk_ReturnsNoHit_WhenNoNonceMeetsTarget()
    {
        // Arrange
        byte[] headerBytes = new byte[80];

        uint hardBits = 0x03000000;
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(72, 4), hardBits);

        uint[] t = GpuMiningWorker.BuildTargetWords(hardBits);
        uint[] ms = MidstateCalculator.ComputeMidstate(headerBytes[..64]);
        uint m0 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(64, 4));
        uint m2 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(72, 4));
        uint m1 = GpuMiningWorker.SwapTimeForShader(GpuMiningWorker.ReadTimeNative(headerBytes));

        using GraphicsDevice device = GraphicsDevice.QueryDevices(static d => d.IsHardwareAccelerated).First();
        using ReadWriteBuffer<uint> resultBuffer = device.AllocateReadWriteBuffer<uint>(2);
        resultBuffer.CopyFrom([0u, 0u]);
        uint[] gpuResult = new uint[2];

        // Act
        bool found = GpuMiningWorker.ExecuteShaderChunk(
            device, ms, m0, m1, m2, t,
            0, 1024, resultBuffer, gpuResult);

        // Assert
        Assert.False(found, "GPU incorrectly reported a hit against an impossible target.");
        Assert.Equal(0u, gpuResult[0]);
    }

    [Fact(DisplayName = nameof(ExecuteShaderChunk_ReturnsNoHit_WhenNonceRangeWrapsPastUInt32Max))]
    public void ExecuteShaderChunk_ReturnsNoHit_WhenNonceRangeWrapsPastUInt32Max()
    {
        // Arrange
        byte[] headerBytes = new byte[80];

        uint hardBits = 0x03000000;
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(72, 4), hardBits);

        uint[] t = GpuMiningWorker.BuildTargetWords(hardBits);
        uint[] ms = MidstateCalculator.ComputeMidstate(headerBytes[..64]);
        uint m0 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(64, 4));
        uint m2 = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(72, 4));
        uint m1 = GpuMiningWorker.SwapTimeForShader(GpuMiningWorker.ReadTimeNative(headerBytes));

        const long nonceSearchStart = 0xFFFF_FF00L;
        const long candidateCount = 0x200L;

        using GraphicsDevice device = GraphicsDevice.QueryDevices(static d => d.IsHardwareAccelerated).First();
        using ReadWriteBuffer<uint> resultBuffer = device.AllocateReadWriteBuffer<uint>(2);
        resultBuffer.CopyFrom([0u, 0u]);
        uint[] gpuResult = new uint[2];

        // Act
        bool found = GpuMiningWorker.ExecuteShaderChunk(
            device, ms, m0, m1, m2, t,
            nonceSearchStart, candidateCount, resultBuffer, gpuResult);

        // Assert
        Assert.False(found, "GPU incorrectly reported a hit while scanning a wrapped nonce range against an impossible target.");
        Assert.Equal(0u, gpuResult[0]);
        Assert.Equal(0u, gpuResult[1]);
    }
}