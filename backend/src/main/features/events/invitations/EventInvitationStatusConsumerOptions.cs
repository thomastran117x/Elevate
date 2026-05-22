using backend.main.application.environment;

namespace backend.main.features.events.invitations;

public sealed record EventInvitationStatusConsumerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId)
{
    public static EventInvitationStatusConsumerOptions FromEnvironment() => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.EmailStatusTopic, nameof(EnvironmentSetting.EmailStatusTopic)),
        Require(EnvironmentSetting.EmailStatusGroupId, nameof(EnvironmentSetting.EmailStatusGroupId)));

    private static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for invitation delivery status consumption.");
    }
}
