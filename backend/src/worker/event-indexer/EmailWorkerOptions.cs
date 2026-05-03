using backend.main.configurations.environment;

namespace backend.worker.event_indexer;

public sealed record EmailWorkerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic,
    string? SmtpServer,
    int SmtpPort,
    string? Username,
    string? Password,
    string FrontendBaseUrl)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SmtpServer)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);

    public static EmailWorkerOptions FromEnvironment() => new(
        EventIndexerOptions.Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        "eventxperience-email",
        "email-worker",
        "eventxperience-email-dlq",
        EnvironmentSetting.SmtpServer,
        EnvironmentSetting.SmtpPort,
        EnvironmentSetting.Email,
        EnvironmentSetting.Password,
        EnvironmentSetting.FrontendBaseUrl
    );
}
