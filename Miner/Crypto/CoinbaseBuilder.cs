using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

[SuppressMessage("Design", "CA1002:Do not expose generic lists")]
internal static class CoinbaseBuilder
{
    internal static readonly byte[] MinerText = Encoding.ASCII.GetBytes("/ComputeSharpMiner/");

    public static (byte[] TxIdBytes, byte[] FullWitnessBytes) Build(long blockHeight, long coinbaseValue, string? defaultWitnessCommitment, string payoutScriptHex, uint extraNonce)
    {
        if (string.IsNullOrWhiteSpace(defaultWitnessCommitment))
            throw new InvalidOperationException("Block template missing default_witness_commitment. Ensure bitcoind is synced and passing 'segwit' rules.");

        if (blockHeight < 0)
            throw new InvalidOperationException("Block height cannot be negative.");

        // 1. Build BIP34 ScriptSig (first item must be canonical CScriptNum encoding of height)
        List<byte> scriptSig = [];
        WriteBip34Height(scriptSig, blockHeight);

        byte[] extraNonceBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(extraNonceBytes, extraNonce);
        WriteScriptPushDataLength(scriptSig, extraNonceBytes.Length);
        scriptSig.AddRange(extraNonceBytes);

        WriteScriptPushDataLength(scriptSig, MinerText.Length);
        scriptSig.AddRange(MinerText);

        byte[] payoutScript = Convert.FromHexString(payoutScriptHex);

        ReadOnlySpan<char> wcSpan = defaultWitnessCommitment.AsSpan();
        if (wcSpan.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) wcSpan = wcSpan[2..];
        byte[] witnessCommitment = Convert.FromHexString(wcSpan);

        byte[] versionBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(versionBytes, 1u);

        byte[] valueBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(valueBytes, coinbaseValue);

        byte[] zeroValueBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(zeroValueBytes, 0L);

        // 2. Build the Legacy (Non-Witness) Transaction (For Merkle Tree Hashing)
        List<byte> txIdData = [];
        txIdData.AddRange(versionBytes);
        txIdData.Add(1);
        txIdData.AddRange(new byte[32]);
        txIdData.AddRange([0xff, 0xff, 0xff, 0xff]);

        WriteVariableInt(txIdData, scriptSig.Count);
        txIdData.AddRange(scriptSig);
        txIdData.AddRange([0xff, 0xff, 0xff, 0xff]);

        txIdData.Add(2);

        txIdData.AddRange(valueBytes);
        WriteVariableInt(txIdData, payoutScript.Length);
        txIdData.AddRange(payoutScript);

        txIdData.AddRange(zeroValueBytes);
        WriteVariableInt(txIdData, witnessCommitment.Length);
        txIdData.AddRange(witnessCommitment);

        txIdData.AddRange([0, 0, 0, 0]);

        // 3. Build the Full Witness Transaction (For Network Submission)
        List<byte> witnessData = [];
        witnessData.AddRange(versionBytes);
        witnessData.Add(0x00);
        witnessData.Add(0x01);

        witnessData.Add(1);
        witnessData.AddRange(new byte[32]);
        witnessData.AddRange([0xff, 0xff, 0xff, 0xff]);
        WriteVariableInt(witnessData, scriptSig.Count);
        witnessData.AddRange(scriptSig);
        witnessData.AddRange([0xff, 0xff, 0xff, 0xff]);

        witnessData.Add(2);
        witnessData.AddRange(valueBytes);
        WriteVariableInt(witnessData, payoutScript.Length);
        witnessData.AddRange(payoutScript);
        witnessData.AddRange(zeroValueBytes);
        WriteVariableInt(witnessData, witnessCommitment.Length);
        witnessData.AddRange(witnessCommitment);

        witnessData.Add(0x01);
        witnessData.Add(0x20);
        witnessData.AddRange(new byte[32]);

        witnessData.AddRange([0, 0, 0, 0]);

        return (txIdData.ToArray(), witnessData.ToArray());
    }

    private static void WriteBip34Height(List<byte> scriptSig, long blockHeight)
    {
        if (blockHeight == 0)
        {
            scriptSig.Add(0x00); // OP_0
            return;
        }

        if (blockHeight is >= 1 and <= 16)
        {
            scriptSig.Add((byte)(0x50 + blockHeight)); // OP_1..OP_16
            return;
        }

        List<byte> heightBytes = [];
        long h = blockHeight;
        while (h > 0)
        {
            heightBytes.Add((byte)(h & 0xff));
            h >>= 8;
        }

        if ((heightBytes[^1] & 0x80) != 0)
        {
            heightBytes.Add(0x00);
        }

        WriteScriptPushDataLength(scriptSig, heightBytes.Count);
        scriptSig.AddRange(heightBytes);
    }

    private static void WriteScriptPushDataLength(List<byte> scriptSig, int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        if (length <= 75)
        {
            scriptSig.Add((byte)length);
        }
        else if (length <= byte.MaxValue)
        {
            scriptSig.Add(0x4c); // OP_PUSHDATA1
            scriptSig.Add((byte)length);
        }
        else if (length <= ushort.MaxValue)
        {
            scriptSig.Add(0x4d); // OP_PUSHDATA2
            scriptSig.Add((byte)length);
            scriptSig.Add((byte)(length >> 8));
        }
        else
        {
            scriptSig.Add(0x4e); // OP_PUSHDATA4
            scriptSig.Add((byte)length);
            scriptSig.Add((byte)(length >> 8));
            scriptSig.Add((byte)(length >> 16));
            scriptSig.Add((byte)(length >> 24));
        }
    }

    private static void WriteVariableInt(List<byte> list, long value)
    {
        if (value < 0xfd)
        {
            list.Add((byte)value);
        }
        else if (value <= 0xffff)
        {
            list.Add(0xfd);
            list.Add((byte)value);
            list.Add((byte)(value >> 8));
        }
        else if (value <= 0xffffffff)
        {
            list.Add(0xfe);
            list.Add((byte)value);
            list.Add((byte)(value >> 8));
            list.Add((byte)(value >> 16));
            list.Add((byte)(value >> 24));
        }
        else
        {
            list.Add(0xff);
            list.Add((byte)value);
            list.Add((byte)(value >> 8));
            list.Add((byte)(value >> 16));
            list.Add((byte)(value >> 24));
            list.Add((byte)(value >> 32));
            list.Add((byte)(value >> 40));
            list.Add((byte)(value >> 48));
            list.Add((byte)(value >> 56));
        }
    }
}
