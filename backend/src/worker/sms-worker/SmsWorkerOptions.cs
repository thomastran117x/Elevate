using backend.main.application.environment;

namespace backend.worker.sms_worker;

public sealed record SmsWorkerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic,
    string? AccountSid,
    string? AuthToken,
    string? MessagingServiceSid,
    string? FromPhoneNumber)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && (
            !string.IsNullOrWhiteSpace(MessagingServiceSid)
            || !string.IsNullOrWhiteSpace(FromPhoneNumber)
        );

    public static SmsWorkerOptions FromEnvironment() => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.SmsTopic, nameof(EnvironmentSetting.SmsTopic)),
        Require(EnvironmentSetting.SmsGroupId, nameof(EnvironmentSetting.SmsGroupId)),
        Require(EnvironmentSetting.SmsDlqTopic, nameof(EnvironmentSetting.SmsDlqTopic)),
        EnvironmentSetting.TwilioAccountSid,
        EnvironmentSetting.TwilioAuthToken,
        EnvironmentSetting.TwilioMessagingServiceSid,
        EnvironmentSetting.TwilioFromPhoneNumber
    );

    private static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the sms worker.");
    }
}
