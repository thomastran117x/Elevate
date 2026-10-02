namespace backend.main.shared.storage;

/// <summary>
/// A forward-only view of another stream that refuses to yield more than a fixed number of
/// bytes, throwing <see cref="LimitExceededException"/> on the first read that would go past it.
/// </summary>
/// <remarks>
/// Lets a download be handed straight to <c>IImageProcessor</c>, which buffers its input once,
/// inside a processing slot, while still guaranteeing nothing over the size cap is buffered. The
/// stored length was already checked, but that is a separate request from the download; this
/// enforces the cap on the bytes actually read.
/// </remarks>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    public BoundedReadStream(Stream inner, long limit)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        // One byte more than the remaining allowance, so a stream that ends exactly at the
        // limit is accepted and one with anything after it is not.
        var read = _inner.Read(buffer[..Allowance(buffer.Length)]);
        return Count(read);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer[..Allowance(buffer.Length)], cancellationToken);
        return Count(read);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await base.DisposeAsync();
    }

    private int Allowance(int requested) =>
        (int)Math.Min(requested, _limit - _read + 1);

    private int Count(int read)
    {
        _read += read;
        if (_read > _limit)
            throw new LimitExceededException();

        return read;
    }

    /// <summary>The underlying stream holds more than the limit.</summary>
    internal sealed class LimitExceededException : IOException
    {
        public LimitExceededException()
            : base("The stream is longer than its permitted length.")
        {
        }
    }
}
