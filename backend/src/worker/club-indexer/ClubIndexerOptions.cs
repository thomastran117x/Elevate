using backend.main.application.environment;

namespace backend.worker.club_indexer;

public sealed record ClubIndexerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic)
{
    public static ClubIndexerOptions FromEnvironment() => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.ClubIndexTopic, nameof(EnvironmentSetting.ClubIndexTopic)),
        Require(EnvironmentSetting.ClubIndexGroupId, nameof(EnvironmentSetting.ClubIndexGroupId)),
        Require(EnvironmentSetting.ClubIndexDlqTopic, nameof(EnvironmentSetting.ClubIndexDlqTopic))
    );

    internal static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the club indexer worker.");
    }
}
