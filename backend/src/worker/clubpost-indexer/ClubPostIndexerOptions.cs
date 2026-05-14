using backend.main.application.environment;

namespace backend.worker.clubpost_indexer;

public sealed record ClubPostIndexerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic)
{
    public static ClubPostIndexerOptions FromEnvironment() => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.ClubPostIndexTopic, nameof(EnvironmentSetting.ClubPostIndexTopic)),
        Require(EnvironmentSetting.ClubPostIndexGroupId, nameof(EnvironmentSetting.ClubPostIndexGroupId)),
        Require(EnvironmentSetting.ClubPostIndexDlqTopic, nameof(EnvironmentSetting.ClubPostIndexDlqTopic))
    );

    private static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the club post indexer worker.");
    }
}
