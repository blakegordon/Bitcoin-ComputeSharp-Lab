using ComputeSharp;

[ThreadGroupSize(256, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct HeaderNonceHashShader(
    uint ms0, uint ms1, uint ms2, uint ms3, uint ms4, uint ms5, uint ms6, uint ms7, // CPU-computed "midstate" (first 64 bytes do not change while iterating the nonce)
    uint m0, uint m1, uint m2,                                                      // message words
    uint nonceBase, uint maxThreads, uint strideX,
    uint t0, uint t1, uint t2, uint t3, uint t4, uint t5, uint t6, uint t7,         // target threshold (must fall under this value to win)
    ReadWriteBuffer<uint> result) : IComputeShader
{
    public void Execute()
    {
        uint threadIndex = (uint)ThreadIds.Y * strideX + (uint)ThreadIds.X;

        if (maxThreads != 0 && threadIndex >= maxThreads) return;

        uint nativeNonce = nonceBase + threadIndex;
        uint swappedNonce = ByteSwap(nativeNonce);

        uint state0 = ms0; uint state1 = ms1; uint state2 = ms2; uint state3 = ms3;
        uint state4 = ms4; uint state5 = ms5; uint state6 = ms6; uint state7 = ms7;

        // Splitting blocks allows HLSL dead-code elimination
        ProcessBlock1(ref state0, ref state1, ref state2, ref state3, ref state4, ref state5, ref state6, ref state7, m0, m1, m2, swappedNonce);

        uint secondState0 = 0x6A09E667u; uint secondState1 = 0xBB67AE85u; uint secondState2 = 0x3C6EF372u; uint secondState3 = 0xA54FF53Au;
        uint secondState4 = 0x510E527Fu; uint secondState5 = 0x9B05688Cu; uint secondState6 = 0x1F83D9ABu; uint secondState7 = 0x5BE0CD19u;

        ProcessBlock2(ref secondState0, ref secondState1, ref secondState2, ref secondState3, ref secondState4, ref secondState5, ref secondState6, ref secondState7, state0, state1, state2, state3, state4, state5, state6, state7);

        bool meetsTarget = false;
        uint h0 = ByteSwap(secondState7);

        if (h0 < t0) meetsTarget = true;
        else if (h0 == t0)
        {
            uint h1 = ByteSwap(secondState6);
            if (h1 < t1) meetsTarget = true;
            else if (h1 == t1)
            {
                uint h2 = ByteSwap(secondState5);
                if (h2 < t2) meetsTarget = true;
                else if (h2 == t2)
                {
                    uint h3 = ByteSwap(secondState4);
                    if (h3 < t3) meetsTarget = true;
                    else if (h3 == t3)
                    {
                        uint h4 = ByteSwap(secondState3);
                        if (h4 < t4) meetsTarget = true;
                        else if (h4 == t4)
                        {
                            uint h5 = ByteSwap(secondState2);
                            if (h5 < t5) meetsTarget = true;
                            else if (h5 == t5)
                            {
                                uint h6 = ByteSwap(secondState1);
                                if (h6 < t6) meetsTarget = true;
                                else if (h6 == t6)
                                {
                                    uint h7 = ByteSwap(secondState0);
                                    if (h7 <= t7) meetsTarget = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (meetsTarget)
        {
            result[0] = 1;
            result[1] = nativeNonce;
        }
    }

    internal static uint ByteSwap(uint value)
    {
        value = (value << 16) | (value >> 16);
        return ((value & 0xFF00FF00u) >> 8) | ((value & 0x00FF00FFu) << 8);
    }

    // HLSL Compiler will propagate literal 0s and drastically optimize the message schedule
    private static void ProcessBlock1(ref uint state0, ref uint state1, ref uint state2, ref uint state3, ref uint state4, ref uint state5, ref uint state6, ref uint state7, uint w0, uint w1, uint w2, uint w3)
    {
        uint a = state0, b = state1, c = state2, d = state3, e = state4, f = state5, g = state6, h = state7;
        uint temp1, temp2;

        // Literal constants for Block 1
        uint w4 = 0x80000000u, w5 = 0u, w6 = 0u, w7 = 0u, w8 = 0u, w9 = 0u, w10 = 0u, w11 = 0u, w12 = 0u, w13 = 0u, w14 = 0u, w15 = 0x00000280u;

        // 0..15
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x428A2F98u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x71374491u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xB5C0FBCFu + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xE9B5DBA5u + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x3956C25Bu + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x59F111F1u + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x923F82A4u + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xAB1C5ED5u + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD807AA98u + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x12835B01u + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x243185BEu + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x550C7DC3u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x72BE5D74u + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x80DEB1FEu + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x9BDC06A7u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC19BF174u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        // 16..31
        w0 += Sigma0Small(w1) + w9 + Sigma1Small(w14); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xE49B69C1u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w1 += Sigma0Small(w2) + w10 + Sigma1Small(w15); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xEFBE4786u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w2 += Sigma0Small(w3) + w11 + Sigma1Small(w0); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x0FC19DC6u + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w3 += Sigma0Small(w4) + w12 + Sigma1Small(w1); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x240CA1CCu + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w4 += Sigma0Small(w5) + w13 + Sigma1Small(w2); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x2DE92C6Fu + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w5 += Sigma0Small(w6) + w14 + Sigma1Small(w3); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x4A7484AAu + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w6 += Sigma0Small(w7) + w15 + Sigma1Small(w4); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x5CB0A9DCu + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w7 += Sigma0Small(w8) + w0 + Sigma1Small(w5); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x76F988DAu + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w8 += Sigma0Small(w9) + w1 + Sigma1Small(w6); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x983E5152u + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w9 += Sigma0Small(w10) + w2 + Sigma1Small(w7); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA831C66Du + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w10 += Sigma0Small(w11) + w3 + Sigma1Small(w8); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xB00327C8u + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w11 += Sigma0Small(w12) + w4 + Sigma1Small(w9); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xBF597FC7u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w12 += Sigma0Small(w13) + w5 + Sigma1Small(w10); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC6E00BF3u + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w13 += Sigma0Small(w14) + w6 + Sigma1Small(w11); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD5A79147u + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w14 += Sigma0Small(w15) + w7 + Sigma1Small(w12); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x06CA6351u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w15 += Sigma0Small(w0) + w8 + Sigma1Small(w13); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x14292967u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        // 32..47
        w0 += Sigma0Small(w1) + w9 + Sigma1Small(w14); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x27B70A85u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w1 += Sigma0Small(w2) + w10 + Sigma1Small(w15); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x2E1B2138u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w2 += Sigma0Small(w3) + w11 + Sigma1Small(w0); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x4D2C6DFCu + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w3 += Sigma0Small(w4) + w12 + Sigma1Small(w1); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x53380D13u + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w4 += Sigma0Small(w5) + w13 + Sigma1Small(w2); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x650A7354u + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w5 += Sigma0Small(w6) + w14 + Sigma1Small(w3); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x766A0ABBu + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w6 += Sigma0Small(w7) + w15 + Sigma1Small(w4); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x81C2C92Eu + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w7 += Sigma0Small(w8) + w0 + Sigma1Small(w5); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x92722C85u + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w8 += Sigma0Small(w9) + w1 + Sigma1Small(w6); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA2BFE8A1u + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w9 += Sigma0Small(w10) + w2 + Sigma1Small(w7); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA81A664Bu + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w10 += Sigma0Small(w11) + w3 + Sigma1Small(w8); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC24B8B70u + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w11 += Sigma0Small(w12) + w4 + Sigma1Small(w9); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC76C51A3u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w12 += Sigma0Small(w13) + w5 + Sigma1Small(w10); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD192E819u + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w13 += Sigma0Small(w14) + w6 + Sigma1Small(w11); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD6990624u + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w14 += Sigma0Small(w15) + w7 + Sigma1Small(w12); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xF40E3585u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w15 += Sigma0Small(w0) + w8 + Sigma1Small(w13); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x106AA070u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        // 48..63
        w0 += Sigma0Small(w1) + w9 + Sigma1Small(w14); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x19A4C116u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w1 += Sigma0Small(w2) + w10 + Sigma1Small(w15); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x1E376C08u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w2 += Sigma0Small(w3) + w11 + Sigma1Small(w0); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x2748774Cu + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w3 += Sigma0Small(w4) + w12 + Sigma1Small(w1); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x34B0BCB5u + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w4 += Sigma0Small(w5) + w13 + Sigma1Small(w2); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x391C0CB3u + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w5 += Sigma0Small(w6) + w14 + Sigma1Small(w3); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x4ED8AA4Au + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w6 += Sigma0Small(w7) + w15 + Sigma1Small(w4); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x5B9CCA4Fu + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w7 += Sigma0Small(w8) + w0 + Sigma1Small(w5); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x682E6FF3u + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w8 += Sigma0Small(w9) + w1 + Sigma1Small(w6); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x748F82EEu + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w9 += Sigma0Small(w10) + w2 + Sigma1Small(w7); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x78A5636Fu + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w10 += Sigma0Small(w11) + w3 + Sigma1Small(w8); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x84C87814u + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w11 += Sigma0Small(w12) + w4 + Sigma1Small(w9); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x8CC70208u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w12 += Sigma0Small(w13) + w5 + Sigma1Small(w10); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x90BEFFFAu + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w13 += Sigma0Small(w14) + w6 + Sigma1Small(w11); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA4506CEBu + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w14 += Sigma0Small(w15) + w7 + Sigma1Small(w12); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xBEF9A3F7u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w15 += Sigma0Small(w0) + w8 + Sigma1Small(w13); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC67178F2u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        state0 += a; state1 += b; state2 += c; state3 += d;
        state4 += e; state5 += f; state6 += g; state7 += h;
    }

    private static void ProcessBlock2(ref uint state0, ref uint state1, ref uint state2, ref uint state3, ref uint state4, ref uint state5, ref uint state6, ref uint state7, uint w0, uint w1, uint w2, uint w3, uint w4, uint w5, uint w6, uint w7)
    {
        uint a = state0, b = state1, c = state2, d = state3, e = state4, f = state5, g = state6, h = state7;
        uint temp1, temp2;

        uint w8 = 0x80000000u, w9 = 0u, w10 = 0u, w11 = 0u, w12 = 0u, w13 = 0u, w14 = 0u, w15 = 0x00000100u;

        // 0..15
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x428A2F98u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x71374491u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xB5C0FBCFu + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xE9B5DBA5u + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x3956C25Bu + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x59F111F1u + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x923F82A4u + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xAB1C5ED5u + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD807AA98u + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x12835B01u + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x243185BEu + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x550C7DC3u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x72BE5D74u + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x80DEB1FEu + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x9BDC06A7u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC19BF174u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        // 16..31
        w0 += Sigma0Small(w1) + w9 + Sigma1Small(w14); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xE49B69C1u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w1 += Sigma0Small(w2) + w10 + Sigma1Small(w15); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xEFBE4786u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w2 += Sigma0Small(w3) + w11 + Sigma1Small(w0); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x0FC19DC6u + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w3 += Sigma0Small(w4) + w12 + Sigma1Small(w1); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x240CA1CCu + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w4 += Sigma0Small(w5) + w13 + Sigma1Small(w2); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x2DE92C6Fu + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w5 += Sigma0Small(w6) + w14 + Sigma1Small(w3); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x4A7484AAu + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w6 += Sigma0Small(w7) + w15 + Sigma1Small(w4); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x5CB0A9DCu + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w7 += Sigma0Small(w8) + w0 + Sigma1Small(w5); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x76F988DAu + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w8 += Sigma0Small(w9) + w1 + Sigma1Small(w6); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x983E5152u + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w9 += Sigma0Small(w10) + w2 + Sigma1Small(w7); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA831C66Du + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w10 += Sigma0Small(w11) + w3 + Sigma1Small(w8); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xB00327C8u + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w11 += Sigma0Small(w12) + w4 + Sigma1Small(w9); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xBF597FC7u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w12 += Sigma0Small(w13) + w5 + Sigma1Small(w10); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC6E00BF3u + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w13 += Sigma0Small(w14) + w6 + Sigma1Small(w11); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD5A79147u + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w14 += Sigma0Small(w15) + w7 + Sigma1Small(w12); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x06CA6351u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w15 += Sigma0Small(w0) + w8 + Sigma1Small(w13); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x14292967u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        // 32..47
        w0 += Sigma0Small(w1) + w9 + Sigma1Small(w14); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x27B70A85u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w1 += Sigma0Small(w2) + w10 + Sigma1Small(w15); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x2E1B2138u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w2 += Sigma0Small(w3) + w11 + Sigma1Small(w0); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x4D2C6DFCu + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w3 += Sigma0Small(w4) + w12 + Sigma1Small(w1); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x53380D13u + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w4 += Sigma0Small(w5) + w13 + Sigma1Small(w2); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x650A7354u + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w5 += Sigma0Small(w6) + w14 + Sigma1Small(w3); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x766A0ABBu + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w6 += Sigma0Small(w7) + w15 + Sigma1Small(w4); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x81C2C92Eu + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w7 += Sigma0Small(w8) + w0 + Sigma1Small(w5); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x92722C85u + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w8 += Sigma0Small(w9) + w1 + Sigma1Small(w6); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA2BFE8A1u + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w9 += Sigma0Small(w10) + w2 + Sigma1Small(w7); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA81A664Bu + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w10 += Sigma0Small(w11) + w3 + Sigma1Small(w8); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC24B8B70u + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w11 += Sigma0Small(w12) + w4 + Sigma1Small(w9); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC76C51A3u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w12 += Sigma0Small(w13) + w5 + Sigma1Small(w10); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD192E819u + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w13 += Sigma0Small(w14) + w6 + Sigma1Small(w11); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xD6990624u + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w14 += Sigma0Small(w15) + w7 + Sigma1Small(w12); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xF40E3585u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w15 += Sigma0Small(w0) + w8 + Sigma1Small(w13); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x106AA070u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        // 48..63
        w0 += Sigma0Small(w1) + w9 + Sigma1Small(w14); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x19A4C116u + w0; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w1 += Sigma0Small(w2) + w10 + Sigma1Small(w15); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x1E376C08u + w1; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w2 += Sigma0Small(w3) + w11 + Sigma1Small(w0); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x2748774Cu + w2; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w3 += Sigma0Small(w4) + w12 + Sigma1Small(w1); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x34B0BCB5u + w3; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w4 += Sigma0Small(w5) + w13 + Sigma1Small(w2); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x391C0CB3u + w4; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w5 += Sigma0Small(w6) + w14 + Sigma1Small(w3); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x4ED8AA4Au + w5; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w6 += Sigma0Small(w7) + w15 + Sigma1Small(w4); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x5B9CCA4Fu + w6; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w7 += Sigma0Small(w8) + w0 + Sigma1Small(w5); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x682E6FF3u + w7; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w8 += Sigma0Small(w9) + w1 + Sigma1Small(w6); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x748F82EEu + w8; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w9 += Sigma0Small(w10) + w2 + Sigma1Small(w7); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x78A5636Fu + w9; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w10 += Sigma0Small(w11) + w3 + Sigma1Small(w8); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x84C87814u + w10; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w11 += Sigma0Small(w12) + w4 + Sigma1Small(w9); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x8CC70208u + w11; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w12 += Sigma0Small(w13) + w5 + Sigma1Small(w10); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0x90BEFFFAu + w12; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w13 += Sigma0Small(w14) + w6 + Sigma1Small(w11); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xA4506CEBu + w13; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w14 += Sigma0Small(w15) + w7 + Sigma1Small(w12); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xBEF9A3F7u + w14; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
        w15 += Sigma0Small(w0) + w8 + Sigma1Small(w13); temp1 = h + Sigma1(e) + Choose(e, f, g) + 0xC67178F2u + w15; temp2 = Sigma0(a) + Majority(a, b, c); h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;

        state0 += a; state1 += b; state2 += c; state3 += d;
        state4 += e; state5 += f; state6 += g; state7 += h;
    }

    private static uint Choose(uint x, uint y, uint z) => z ^ (x & (y ^ z));
    private static uint Majority(uint x, uint y, uint z) => (x & y) | (z & (x | y));

    private static uint Sigma0(uint x) => RotateRight(x, 2) ^ RotateRight(x, 13) ^ RotateRight(x, 22);
    private static uint Sigma1(uint x) => RotateRight(x, 6) ^ RotateRight(x, 11) ^ RotateRight(x, 25);
    private static uint Sigma0Small(uint x) => RotateRight(x, 7) ^ RotateRight(x, 18) ^ (x >> 3);
    private static uint Sigma1Small(uint x) => RotateRight(x, 17) ^ RotateRight(x, 19) ^ (x >> 10);

    private static uint RotateRight(uint value, int amount) => (value >> amount) | (value << (32 - amount));
}
