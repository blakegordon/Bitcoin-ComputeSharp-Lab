using System.Globalization;

internal sealed record BlockHeader(uint Version, byte[] PreviousBlockHashBytes, byte[] MerkleRootBytes, uint Time, uint Bits, uint Nonce, byte[] FullWitnessCoinbaseBytes)
{
    public static BlockHeader Create(GetBlockTemplateResponse template, string payoutScriptHex, uint extraNonce, byte[][] precomputedBranches)
    {
        byte[] prevHashBytes = ParseBitcoinHash(template.PreviousBlockHash);

        var (txIdBytes, fullWitnessBytes) = CoinbaseBuilder.Build(template.Height, template.CoinbaseValue, template.DefaultWitnessCommitment, payoutScriptHex, extraNonce);

        byte[] cbHash = DoubleSha256.Compute(txIdBytes);
        byte[] merkleRootBytes = ComputeMerkleRootFromBranches(cbHash, precomputedBranches);

        return new BlockHeader(template.Version, prevHashBytes, merkleRootBytes, template.CurTime, ParseCompactBits(template.Bits), 0, fullWitnessBytes);
    }

    public static byte[][] PrecomputeMerkleBranches(GetBlockTemplateResponse template)
    {
        List<byte[]> level = [new byte[32]]; // Spacer for coinbase
        if (template.Transactions != null)
        {
            foreach (var tx in template.Transactions)
            {
                if (string.IsNullOrWhiteSpace(tx.TxId)) continue;
                ReadOnlySpan<char> span = tx.TxId.AsSpan();
                if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) span = span[2..];
                byte[] txHash = Convert.FromHexString(span);
                Array.Reverse(txHash);
                level.Add(txHash);
            }
        }

        List<byte[]> branches = [];
        while (level.Count > 1)
        {
            // Sibling of coinbase is index 1 (or 0 if it's duplicated)
            branches.Add(level.Count > 1 ? level[1] : level[0]);

            // Pre-allocate capacity to prevent array resizing overhead
            List<byte[]> nextLevel = [with((level.Count + 1) / 2)];

            for (int i = 0; i < level.Count; i += 2)
            {
                byte[] left = level[i];
                byte[] right = (i + 1 < level.Count) ? level[i + 1] : left;

                if (i == 0)
                {
                    nextLevel.Add(new byte[32]); // keep spacer
                }
                else
                {
                    byte[] combined = new byte[64];
                    Buffer.BlockCopy(left, 0, combined, 0, 32);
                    Buffer.BlockCopy(right, 0, combined, 32, 32);
                    nextLevel.Add(DoubleSha256.Compute(combined));
                }
            }
            level = nextLevel;
        }

        return [.. branches];
    }

    private static byte[] ComputeMerkleRootFromBranches(byte[] coinbaseHash, byte[][] branches)
    {
        byte[] root = coinbaseHash;
        byte[] combined = new byte[64];
        foreach (byte[] branch in branches)
        {
            Buffer.BlockCopy(root, 0, combined, 0, 32);
            Buffer.BlockCopy(branch, 0, combined, 32, 32);
            root = DoubleSha256.Compute(combined);
        }
        return root;
    }

    public byte[] ToBytes()
    {
        byte[] bytes = new byte[80];
        WriteUInt32(bytes, 0, Version);
        Array.Copy(PreviousBlockHashBytes, 0, bytes, 4, 32);
        Array.Copy(MerkleRootBytes, 0, bytes, 36, 32);
        WriteUInt32(bytes, 68, Time);
        WriteUInt32(bytes, 72, Bits);
        WriteUInt32(bytes, 76, Nonce);
        return bytes;
    }

    public uint[] ToUintWords()
    {
        uint[] words = new uint[80 >> 2];
        Buffer.BlockCopy(ToBytes(), 0, words, 0, 80);
        return words;
    }

    private static void WriteUInt32(byte[] destination, int offset, uint value)
    {
        destination[offset] = (byte)(value & 0xFF);
        destination[offset + 1] = (byte)((value >> 8) & 0xFF);
        destination[offset + 2] = (byte)((value >> 16) & 0xFF);
        destination[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    public static byte[] ParseBitcoinHash(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return new byte[32];
        ReadOnlySpan<char> span = hex.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) span = span[2..];
        byte[] bytes = Convert.FromHexString(span);
        if (bytes.Length > 32) bytes = bytes[..32];
        if (bytes.Length < 32) Array.Resize(ref bytes, 32);
        Array.Reverse(bytes);
        return bytes;
    }

    public static uint ParseCompactBits(string? bits)
    {
        if (string.IsNullOrWhiteSpace(bits)) throw new InvalidOperationException("No bits field in template.");
        return uint.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}