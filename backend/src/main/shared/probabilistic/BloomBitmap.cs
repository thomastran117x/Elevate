namespace backend.main.shared.probabilistic;

/// <summary>
/// A fixed-width bit array whose byte layout is identical to a Redis bitmap, so a local
/// filter and the shared Redis key can be merged byte-for-byte without a translation step.
/// </summary>
/// <remarks>
/// Redis numbers bits from the most significant bit of the first byte: bit 0 is 0x80 of
/// byte 0, bit 7 is 0x01 of byte 0, bit 8 is 0x80 of byte 1. <see cref="System.Collections.BitArray"/>
/// uses the opposite convention within each byte, which is why this type exists rather than
/// wrapping BitArray.
/// </remarks>
public sealed class BloomBitmap
{
    private readonly object _writeGate = new();
    private readonly byte[] _bytes;

    public BloomBitmap(long bitCount)
    {
        if (bitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "Bit count must be positive.");

        BitCount = bitCount;
        _bytes = new byte[(bitCount + 7) / 8];
    }

    private BloomBitmap(byte[] bytes, long bitCount)
    {
        _bytes = bytes;
        BitCount = bitCount;
    }

    public long BitCount
    {
        get;
    }

    public int ByteCount => _bytes.Length;

    /// <summary>
    /// Rehydrates a bitmap from bytes read out of Redis. Shorter payloads are zero-extended
    /// and longer ones truncated, so a filter whose configured width changed between deploys
    /// degrades to a partial load rather than throwing on startup.
    /// </summary>
    public static BloomBitmap FromBytes(ReadOnlySpan<byte> bytes, long bitCount)
    {
        if (bitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "Bit count must be positive.");

        var buffer = new byte[(bitCount + 7) / 8];
        var copyLength = Math.Min(buffer.Length, bytes.Length);
        bytes[..copyLength].CopyTo(buffer);

        return new BloomBitmap(buffer, bitCount);
    }

    public bool Get(long bitPosition)
    {
        if (bitPosition < 0 || bitPosition >= BitCount)
            throw new ArgumentOutOfRangeException(nameof(bitPosition));

        var value = Volatile.Read(ref _bytes[bitPosition >> 3]);
        return (value & MaskFor(bitPosition)) != 0;
    }

    /// <summary>Returns true when every supplied position is set.</summary>
    public bool GetAll(ReadOnlySpan<long> bitPositions)
    {
        foreach (var position in bitPositions)
        {
            if (!Get(position))
                return false;
        }

        return true;
    }

    public void Set(long bitPosition)
    {
        if (bitPosition < 0 || bitPosition >= BitCount)
            throw new ArgumentOutOfRangeException(nameof(bitPosition));

        // Setting a bit is a read-modify-write, so concurrent setters in the same byte could
        // lose an update without this gate. Reads stay lock-free: a single byte never tears.
        lock (_writeGate)
        {
            _bytes[bitPosition >> 3] |= MaskFor(bitPosition);
        }
    }

    public void SetAll(ReadOnlySpan<long> bitPositions)
    {
        lock (_writeGate)
        {
            foreach (var position in bitPositions)
            {
                if (position < 0 || position >= BitCount)
                    throw new ArgumentOutOfRangeException(nameof(bitPositions));

                _bytes[position >> 3] |= MaskFor(position);
            }
        }
    }

    /// <summary>
    /// ORs another bitmap of the same width into this one. Union is the only merge a bloom
    /// filter permits: bits may be added but never cleared, so merging can lose accuracy but
    /// can never produce a false negative.
    /// </summary>
    public void UnionWith(BloomBitmap other)
    {
        ArgumentNullException.ThrowIfNull(other);

        lock (_writeGate)
        {
            var length = Math.Min(_bytes.Length, other._bytes.Length);
            for (var i = 0; i < length; i++)
                _bytes[i] |= Volatile.Read(ref other._bytes[i]);
        }
    }

    /// <summary>Snapshot of the backing bytes, laid out for a Redis bitmap.</summary>
    public byte[] ToBytes()
    {
        lock (_writeGate)
        {
            return (byte[])_bytes.Clone();
        }
    }

    /// <summary>Number of set bits, used to estimate the live false-positive rate.</summary>
    public long CountSetBits()
    {
        long count = 0;
        for (var i = 0; i < _bytes.Length; i++)
            count += System.Numerics.BitOperations.PopCount(Volatile.Read(ref _bytes[i]));

        return count;
    }

    private static byte MaskFor(long bitPosition) => (byte)(0x80 >> (int)(bitPosition & 7));
}
