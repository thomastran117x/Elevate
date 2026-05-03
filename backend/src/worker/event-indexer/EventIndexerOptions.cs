using backend.main.configurations.environment;

namespace backend.worker.event_indexer;

public sealed record EventIndexerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic)
{
    public static EventIndexerOptions FromEnvironment() => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.EventIndexTopic, nameof(EnvironmentSetting.EventIndexTopic)),
        Require(EnvironmentSetting.EventIndexGroupId, nameof(EnvironmentSetting.EventIndexGroupId)),
        Require(EnvironmentSetting.EventIndexDlqTopic, nameof(EnvironmentSetting.EventIndexDlqTopic))
    );

    internal static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the event indexer worker.");
    }
}
