using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace backend.main.shared.probabilistic;

/// <summary>
/// Derives bit positions for a bloom filter using Kirsch-Mitzenmacher double hashing:
/// position(i) = (h1 + i * h2) mod m, taken from the two halves of one SHA-256 digest.
/// </summary>
/// <remarks>
/// SHA-256 is used purely for its stability, not for secrecy. The bits produced here are
/// written to a Redis bitmap that is shared across processes, across restarts and across
/// deploys, so the mapping from a value to its bit positions must be identical everywhere
/// and forever. <see cref="string.GetHashCode()"/> is randomised per process and would
/// silently corrupt a shared filter; any replacement must be equally deterministic.
/// </remarks>
public static class BloomHash
{
    /// <summary>
    /// Computes the bit positions for <paramref name="value"/> within a filter of
    /// <paramref name="bitCount"/> bits.
    /// </summary>
    /// <param name="target">
    /// Target name (username, club-name, email). Mixed into the digest so the same literal
    /// text maps to different bits in different filters, which keeps the targets independent
    /// even if their bitmaps are ever colocated.
    /// </param>
    /// <param name="value">Already-normalised value. Callers must normalise first.</param>
    /// <param name="bitCount">Filter width in bits.</param>
    /// <param name="hashCount">Number of positions to produce.</param>
    public static long[] GetBitPositions(string target, string value, long bitCount, int hashCount)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentNullException.ThrowIfNull(value);

        if (bitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "Bit count must be positive.");
        if (hashCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(hashCount), "Hash count must be positive.");

        var (h1, h2) = ComputeSeeds(target, value);
        var width = (ulong)bitCount;

        var positions = new long[hashCount];
        for (var i = 0; i < hashCount; i++)
        {
            // unchecked: wrapping is the intended behaviour of double hashing, not an error.
            var combined = unchecked(h1 + ((ulong)i * h2));
            positions[i] = (long)(combined % width);
        }

        return positions;
    }

    private static (ulong H1, ulong H2) ComputeSeeds(string target, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(target) + 1 + Encoding.UTF8.GetByteCount(value);
        var buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];

        var written = Encoding.UTF8.GetBytes(target, buffer);
        // ':' cannot appear in a target name, so the separator makes the pair unambiguous.
        buffer[written++] = (byte)':';
        Encoding.UTF8.GetBytes(value, buffer[written..]);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(buffer, digest);

        var h1 = BinaryPrimitives.ReadUInt64LittleEndian(digest[..8]);
        var h2 = BinaryPrimitives.ReadUInt64LittleEndian(digest[8..16]);

        // An even h2 can walk a short cycle over the filter and revisit the same positions,
        // which costs accuracy. Forcing it odd keeps the stride coprime with any power-of-two
        // width and is the standard guard for this scheme.
        h2 |= 1;

        return (h1, h2);
    }
}
