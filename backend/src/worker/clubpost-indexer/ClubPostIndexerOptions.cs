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
        "clubpost-es-index",
        "clubpost-indexer",
        "clubpost-es-index-dlq"
    );

    private static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the club post indexer worker.");
    }
}
