using System.Buffers.Binary;
using System.Numerics;

namespace VRCX.Vrchat;

/// <summary>
/// The librsync signature VRChat wants beside every uploaded file: what the original's SignFile
/// made with librsync.net (Librsync.ComputeSignature with its defaults). A BLAKE2 signature header
/// (magic 0x72730137, 2048-byte blocks, 32-byte strong sums), then per block the rolling checksum
/// and a BLAKE2b-256 hash, all big-endian.
/// </summary>
public static class RsyncSignature
{
    private const uint Blake2Magic = 0x72730137;
    private const int BlockLength = 2048;
    private const int StrongLength = 32;

    public static byte[] Compute(ReadOnlySpan<byte> data)
    {
        var blocks = (data.Length + BlockLength - 1) / BlockLength;
        var output = new byte[12 + blocks * (4 + StrongLength)];
        BinaryPrimitives.WriteUInt32BigEndian(output, Blake2Magic);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4), BlockLength);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8), StrongLength);
        var at = 12;
        for (var offset = 0; offset < data.Length; offset += BlockLength)
        {
            var block = data.Slice(offset, Math.Min(BlockLength, data.Length - offset));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), RollSum(block));
            Blake2b.Hash(block, output.AsSpan(at + 4, StrongLength));
            at += 4 + StrongLength;
        }
        return output;
    }

    // librsync's rollsum: each byte counts as itself plus 31.
    private static uint RollSum(ReadOnlySpan<byte> block)
    {
        uint s1 = 0, s2 = 0;
        foreach (var b in block)
        {
            s1 += b + 31u;
            s2 += s1;
        }
        return (s2 << 16) | (s1 & 0xffff);
    }
}

/// <summary>Unkeyed BLAKE2b (RFC 7693) with a digest of up to 64 bytes.</summary>
internal static class Blake2b
{
    private static readonly ulong[] Iv =
    [
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
    ];

    private static readonly byte[][] Sigma =
    [
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
        [11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4],
        [7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8],
        [9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13],
        [2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9],
        [12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11],
        [13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10],
        [6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5],
        [10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0],
    ];

    public static void Hash(ReadOnlySpan<byte> data, Span<byte> output)
    {
        if (output.Length is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(output));
        Span<ulong> h = stackalloc ulong[8];
        Iv.CopyTo(h);
        // Parameter block: digest length, no key, fanout 1, depth 1.
        h[0] ^= 0x01010000UL ^ (ulong)output.Length;

        Span<ulong> m = stackalloc ulong[16];
        ulong counter = 0;
        var offset = 0;
        while (data.Length - offset > 128)
        {
            counter += 128;
            Load(data.Slice(offset, 128), m);
            Compress(h, m, counter, false);
            offset += 128;
        }
        Span<byte> last = stackalloc byte[128];
        last.Clear();
        data[offset..].CopyTo(last);
        counter += (ulong)(data.Length - offset);
        Load(last, m);
        Compress(h, m, counter, true);

        Span<byte> digest = stackalloc byte[64];
        for (var i = 0; i < 8; i++) BinaryPrimitives.WriteUInt64LittleEndian(digest[(i * 8)..], h[i]);
        digest[..output.Length].CopyTo(output);
    }

    private static void Load(ReadOnlySpan<byte> block, Span<ulong> m)
    {
        for (var i = 0; i < 16; i++) m[i] = BinaryPrimitives.ReadUInt64LittleEndian(block[(i * 8)..]);
    }

    private static void Compress(Span<ulong> h, ReadOnlySpan<ulong> m, ulong counter, bool final)
    {
        Span<ulong> v = stackalloc ulong[16];
        h.CopyTo(v);
        Iv.CopyTo(v[8..]);
        v[12] ^= counter;
        if (final) v[14] = ~v[14];
        for (var round = 0; round < 12; round++)
        {
            var s = Sigma[round % 10];
            G(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            G(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            G(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            G(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            G(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            G(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            G(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            G(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }
        for (var i = 0; i < 8; i++) h[i] ^= v[i] ^ v[i + 8];
    }

    private static void G(Span<ulong> v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] += v[b] + x;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 32);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 24);
        v[a] += v[b] + y;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 63);
    }
}
