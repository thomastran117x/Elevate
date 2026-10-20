using backend.main.features.auth.history;
using backend.worker.security_history_worker;

using FluentAssertions;

using Microsoft.Extensions.Configuration;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryWorkerOptionsTests
{
    [Fact]
    public void FromConfiguration_ShouldDefaultToNinetyDaysAndTheUnnotifiedHighRiskKinds()
    {
        var options = SecurityHistoryWorkerOptions.FromConfiguration(Configuration());

        options.RetentionDays.Should().Be(90);
        options.PurgeBatchSize.Should().Be(500);
        options.PurgeMaxBatches.Should().Be(20);
        options.PurgeInterval.Should().Be(TimeSpan.FromHours(1));
        options.NotificationsEnabled.Should().BeTrue();
        options.AlertKinds.Should().BeEquivalentTo(
        [
            AccountSecurityEventKind.PasswordChanged,
            AccountSecurityEventKind.ProviderLinked,
            AccountSecurityEventKind.RefreshTokenReuseDetected,
            AccountSecurityEventKind.AccountDisabled,
        ]);
        options.Topic.Should().NotBeNullOrWhiteSpace();
        options.DlqTopic.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FromConfiguration_ShouldReadEveryOverride()
    {
        var options = SecurityHistoryWorkerOptions.FromConfiguration(Configuration(new()
        {
            [SecurityHistoryWorkerOptions.RetentionDaysKey] = "30",
            [SecurityHistoryWorkerOptions.PurgeBatchSizeKey] = "100",
            [SecurityHistoryWorkerOptions.PurgeMaxBatchesKey] = "5",
            [SecurityHistoryWorkerOptions.PurgeIntervalMinutesKey] = "15",
            [SecurityHistoryWorkerOptions.NotificationsEnabledKey] = "false",
            [SecurityHistoryWorkerOptions.AlertKindsKey] = " mfa_disabled , password_changed ",
        }));

        options.RetentionDays.Should().Be(30);
        options.PurgeBatchSize.Should().Be(100);
        options.PurgeMaxBatches.Should().Be(5);
        options.PurgeInterval.Should().Be(TimeSpan.FromMinutes(15));
        options.NotificationsEnabled.Should().BeFalse();
        options.AlertKinds.Should().BeEquivalentTo(
            [AccountSecurityEventKind.MfaDisabled, AccountSecurityEventKind.PasswordChanged]);
    }

    [Fact]
    public void FromConfiguration_ShouldAllowTurningEveryAlertOff()
    {
        var options = SecurityHistoryWorkerOptions.FromConfiguration(Configuration(new()
        {
            [SecurityHistoryWorkerOptions.AlertKindsKey] = "none",
        }));

        options.AlertKinds.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityHistoryWorkerOptions.RetentionDaysKey, "0")]
    [InlineData(SecurityHistoryWorkerOptions.RetentionDaysKey, "91")]
    [InlineData(SecurityHistoryWorkerOptions.RetentionDaysKey, "ninety")]
    [InlineData(SecurityHistoryWorkerOptions.PurgeBatchSizeKey, "0")]
    [InlineData(SecurityHistoryWorkerOptions.PurgeBatchSizeKey, "5001")]
    [InlineData(SecurityHistoryWorkerOptions.PurgeMaxBatchesKey, "0")]
    [InlineData(SecurityHistoryWorkerOptions.PurgeIntervalMinutesKey, "0")]
    [InlineData(SecurityHistoryWorkerOptions.NotificationsEnabledKey, "yes")]
    [InlineData(SecurityHistoryWorkerOptions.AlertKindsKey, "password_changed,login_failed")]
    public void FromConfiguration_ShouldRefuseToStart_OnAnInvalidValue(string key, string value)
    {
        var read = () => SecurityHistoryWorkerOptions.FromConfiguration(Configuration(new() { [key] = value }));

        read.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    [Fact]
    public void Require_ShouldRefuseAnUnsetSetting()
    {
        var read = () => SecurityHistoryWorkerOptions.Require(" ", "SecurityHistoryTopic");

        read.Should().Throw<InvalidOperationException>().WithMessage("*SecurityHistoryTopic*");
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();
}
