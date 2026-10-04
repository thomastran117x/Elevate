using System.Reflection;

using backend.worker.email_worker;
using backend.worker.media_worker;
using backend.worker.sms_worker;

using FluentAssertions;

namespace backend.tests.Unit.Workers;

public class WorkerOptionsFactoryTests
{
    [Fact]
    public void SmsWorkerOptions_FromEnvironment_ShouldUseDefaultTopicsWhenSmsSettingsAreMissing()
    {
        var options = SmsWorkerOptions.FromEnvironment();

        options.Topic.Should().Be("eventxperience-sms");
        options.GroupId.Should().Be("sms-worker");
        options.DlqTopic.Should().Be("eventxperience-sms-dlq");
    }

    [Fact]
    public void EmailWorkerOptions_FromEnvironment_ShouldUseDefaultTopicsWhenEmailSettingsAreMissing()
    {
        var options = EmailWorkerOptions.FromEnvironment();

        options.Topic.Should().Be("eventxperience-email");
        options.GroupId.Should().Be("email-worker");
        options.DlqTopic.Should().Be("eventxperience-email-dlq");
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

    [Fact]
    public void MediaWorkerOptions_FromEnvironment_ShouldUseDefaultTopicsWhenMediaSettingsAreMissing()
    {
        var options = MediaWorkerOptions.FromEnvironment();

        options.Topic.Should().Be("eventxperience-media-validation");
        options.GroupId.Should().Be("media-worker");
        options.DlqTopic.Should().Be("eventxperience-media-validation-dlq");
        options.StatusTopic.Should().Be("eventxperience-media-validation-status");
    }

    [Fact]
    public void MediaWorkerOptions_Require_ShouldTrimConfiguredValues()
    {
        InvokeRequire(typeof(MediaWorkerOptions), "  media-topic  ", "MediaValidationTopic")
            .Should().Be("media-topic");
    }

    [Fact]
    public void MediaWorkerOptions_Require_ShouldThrowWhenValueIsMissing()
    {
        var act = () => InvokeRequire(typeof(MediaWorkerOptions), null, "MediaValidationTopic");

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*MediaValidationTopic must be configured for the media worker*");
    }

    [Theory]
    [InlineData("conn", "public", "quarantine", true)]
    [InlineData(null, "public", "quarantine", false)]
    [InlineData("conn", " ", "quarantine", false)]
    [InlineData("conn", "public", "", false)]
    public void MediaWorkerOptions_IsConfigured_ShouldRequireStorage(
        string? connectionString,
        string? publicContainer,
        string? quarantineContainer,
        bool expected)
    {
        new MediaWorkerOptions("kafka", "topic", "group", "dlq", "status", connectionString, publicContainer, quarantineContainer)
            .IsConfigured.Should().Be(expected);
    }

    private static string InvokeRequire(Type type, string? value, string settingName)
    {
        var method = type.GetMethod("Require", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, [value, settingName])!;
    }
}
