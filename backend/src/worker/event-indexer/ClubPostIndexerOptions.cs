using backend.main.configurations.environment;

namespace backend.worker.event_indexer;

public sealed record ClubPostIndexerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic)
{
    public static ClubPostIndexerOptions FromEnvironment() => new(
        EventIndexerOptions.Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        "clubpost-es-index",
        "clubpost-indexer",
        "clubpost-es-index-dlq"
    );
}
