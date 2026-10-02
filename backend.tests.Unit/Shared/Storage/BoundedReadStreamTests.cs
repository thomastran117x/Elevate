using backend.main.shared.storage;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Storage;

public class BoundedReadStreamTests
{
    [Fact]
    public async Task ShouldYieldEveryByte_WhenTheStreamEndsExactlyAtTheLimit()
    {
        var content = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        await using var bounded = new BoundedReadStream(new MemoryStream(content), 100);
        using var copy = new MemoryStream();

        await bounded.CopyToAsync(copy);

        copy.ToArray().Should().Equal(content);
        bounded.Position.Should().Be(100);
    }

    [Fact]
    public async Task ShouldThrow_AsSoonAsAReadGoesPastTheLimit()
    {
        await using var bounded = new BoundedReadStream(new MemoryStream(new byte[101]), 100);

        var act = () => bounded.CopyToAsync(Stream.Null);

        await act.Should().ThrowAsync<BoundedReadStream.LimitExceededException>();
    }

    [Fact]
    public void ShouldEnforceTheLimit_OnSynchronousReadsToo()
    {
        using var bounded = new BoundedReadStream(new MemoryStream(new byte[10]), 4);
        var buffer = new byte[16];

        var act = () => bounded.Read(buffer, 0, buffer.Length);

        act.Should().Throw<BoundedReadStream.LimitExceededException>();
    }

    [Fact]
    public async Task ShouldNeverAskTheInnerStreamForMoreThanOneByteOverTheLimit()
    {
        var inner = new CountingStream(new byte[1000]);
        await using var bounded = new BoundedReadStream(inner, 10);

        await bounded.Invoking(b => b.CopyToAsync(Stream.Null)).Should().ThrowAsync<BoundedReadStream.LimitExceededException>();

        inner.Delivered.Should().BeLessThanOrEqualTo(11);
    }

    [Fact]
    public void ShouldBeForwardOnlyAndReadOnly()
    {
        using var bounded = new BoundedReadStream(new MemoryStream(new byte[4]), 4);

        bounded.CanSeek.Should().BeFalse();
        bounded.CanWrite.Should().BeFalse();
        bounded.Invoking(b => b.Length).Should().Throw<NotSupportedException>();
        bounded.Invoking(b => b.Position = 1).Should().Throw<NotSupportedException>();
        bounded.Invoking(b => b.Seek(0, SeekOrigin.Begin)).Should().Throw<NotSupportedException>();
        bounded.Invoking(b => b.SetLength(1)).Should().Throw<NotSupportedException>();
        bounded.Invoking(b => b.Write([1], 0, 1)).Should().Throw<NotSupportedException>();
        bounded.Invoking(b => b.Flush()).Should().NotThrow();
    }

    [Fact]
    public async Task ShouldDisposeTheInnerStream()
    {
        var inner = new MemoryStream(new byte[4]);

        await new BoundedReadStream(inner, 4).DisposeAsync();
        new BoundedReadStream(new MemoryStream(), 0).Dispose();

        inner.CanRead.Should().BeFalse();
    }

    [Fact]
    public void ShouldRejectANegativeLimit()
    {
        var act = () => new BoundedReadStream(new MemoryStream(), -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class CountingStream(byte[] content) : MemoryStream(content)
    {
        public long Delivered
        {
            get; private set;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            Delivered += read;
            return read;
        }
    }
}
