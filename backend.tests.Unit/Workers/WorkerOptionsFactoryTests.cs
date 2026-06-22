using System.Reflection;

using backend.worker.email_worker;
using backend.worker.sms_worker;

using FluentAssertions;

namespace backend.tests.Unit.Workers;

public class WorkerOptionsFactoryTests
{
    [Fact]
    public void SmsWorkerOptions_FromEnvironment_ShouldThrowWhenSmsTopicIsMissing()
    {
        var act = () => SmsWorkerOptions.FromEnvironment();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SmsTopic must be configured*");
    }

    [Fact]
    public void EmailWorkerOptions_FromEnvironment_ShouldThrowWhenEmailTopicIsMissing()
    {
        var act = () => EmailWorkerOptions.FromEnvironment();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EmailTopic must be configured*");
    }

    [Fact]
    public void SmsWorkerOptions_Require_ShouldTrimConfiguredValues()
    {
        InvokeRequire(typeof(SmsWorkerOptions), "  sms-topic  ", "SmsTopic")
            .Should().Be("sms-topic");
    }

    [Fact]
    public void SmsWorkerOptions_Require_ShouldThrowWhenValueIsMissing()
    {
        var act = () => InvokeRequire(typeof(SmsWorkerOptions), null, "SmsTopic");

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*SmsTopic must be configured*");
    }

    [Fact]
    public void EmailWorkerOptions_Require_ShouldTrimConfiguredValues()
    {
        InvokeRequire(typeof(EmailWorkerOptions), "  email-topic  ", "EmailTopic")
            .Should().Be("email-topic");
    }

    [Fact]
    public void EmailWorkerOptions_Require_ShouldThrowWhenValueIsMissing()
    {
        var act = () => InvokeRequire(typeof(EmailWorkerOptions), null, "EmailTopic");

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*EmailTopic must be configured*");
    }

    private static string InvokeRequire(Type type, string? value, string settingName)
    {
        var method = type.GetMethod("Require", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, [value, settingName])!;
    }
}
