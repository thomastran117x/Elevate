using backend.worker.sms_worker;

using FluentAssertions;

namespace backend.tests.Unit.Workers.Sms;

public class TransientSmsDeliveryExceptionTests
{
    [Fact]
    public void Constructor_ShouldStoreMessage()
    {
        var exception = new TransientSmsDeliveryException("temporary outage");

        exception.Message.Should().Be("temporary outage");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldStoreInnerException()
    {
        var inner = new InvalidOperationException("root cause");

        var exception = new TransientSmsDeliveryException("temporary outage", inner);

        exception.Message.Should().Be("temporary outage");
        exception.InnerException.Should().BeSameAs(inner);
    }
}
