using System.Buffers.Binary;

internal static class MidstateCalculator
{
    private static readonly uint[] K =
    [
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2
    ];

    /// <summary>
    /// Computes the SHA-256 midstate (the 8 uint chaining variables) after processing the first 64 bytes
    /// of an 80-byte Bitcoin block header.
    /// This matches the first ProcessBlock in the GPU shader when using the same word layout.
    /// </summary>
    /// <param name="headerFirst64">Exactly 64 bytes (the part before the last 16 bytes of the header)</param>
    /// <returns>8 uints representing the midstate (state0..state7)</returns>
    public static uint[] ComputeMidstate(byte[] headerFirst64)
    {
        if (headerFirst64 is null || headerFirst64.Length != 64)
            throw new ArgumentException("headerFirst64 must be exactly 64 bytes", nameof(headerFirst64));

        // Convert 64 bytes to 16 big-endian uint words (same as current GpuMiningWorker does with ReadUInt32BigEndian)
        uint[] w = new uint[16];
        for (int i = 0; i < 16; i++)
        {
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(headerFirst64.AsSpan(i * 4, 4));
        }

        uint a = 0x6A09E667u;
        uint b = 0xBB67AE85u;
        uint c = 0x3C6EF372u;
        uint d = 0xA54FF53Au;
        uint e = 0x510E527Fu;
        uint f = 0x9B05688Cu;
        uint g = 0x1F83D9ABu;
        uint h = 0x5BE0CD19u;

        // Perform one SHA-256 block (same logic as the unrolled ProcessBlock in the shader, first 64 rounds)
        for (int i = 0; i < 64; i++)
        {
            uint wi;
            if (i < 16)
            {
                wi = w[i];
            }
            else
            {
                uint w0 = w[(i - 16) & 15];
                uint w1 = w[(i - 15) & 15];
                uint w9 = w[(i - 7) & 15];
                uint w14 = w[(i - 2) & 15];
                wi = w[i & 15] = w0 + (RotateRight(w1, 7) ^ RotateRight(w1, 18) ^ (w1 >> 3))
                                + w9 + (RotateRight(w14, 17) ^ RotateRight(w14, 19) ^ (w14 >> 10));
            }

            uint temp1 = h + (RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25))
                         + ((e & f) ^ ((~e) & g)) + K[i] + wi;

            uint temp2 = (RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22))
                         + ((a & b) ^ (a & c) ^ (b & c));

            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        // Add to initial state (this produces the midstate)
        uint[] midstate =
        [
            0x6A09E667u + a,
            0xBB67AE85u + b,
            0x3C6EF372u + c,
            0xA54FF53Au + d,
            0x510E527Fu + e,
            0x9B05688Cu + f,
            0x1F83D9ABu + g,
            0x5BE0CD19u + h,
        ];
        return midstate;
    }

    private static uint RotateRight(uint x, int n) => (x >> n) | (x << (32 - n));

    /// <summary>
    /// Convenience: given a full 80-byte header, return the midstate for its first 64 bytes.
    /// </summary>
    public static uint[] ComputeMidstateFromHeader(byte[] header80)
    {
        if (header80 is null || header80.Length != 80)
            throw new ArgumentException("header80 must be 80 bytes", nameof(header80));

        byte[] first64 = new byte[64];
        Buffer.BlockCopy(header80, 0, first64, 0, 64);
        return ComputeMidstate(first64);
    }
}
