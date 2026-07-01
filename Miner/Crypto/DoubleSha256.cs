using System.Security.Cryptography;

internal static class DoubleSha256
{
    /// <summary>
    /// Computes double-SHA256 (SHA256(SHA256(data))) as used throughout Bitcoin.
    /// Returns the 32-byte hash in the same order as the hash function (no reversal here).
    /// </summary>
    public static byte[] Compute(ReadOnlySpan<byte> data)
    {
        Span<byte> first = stackalloc byte[32];
        SHA256.HashData(data, first);

        byte[] final = new byte[32];
        SHA256.HashData(first, final);
        return final;
    }
}
