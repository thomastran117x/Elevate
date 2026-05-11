using backend.main.application.environment;

namespace backend.worker.email_worker;

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
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        "eventxperience-email",
        "email-worker",
        "eventxperience-email-dlq",
        EnvironmentSetting.SmtpServer,
        EnvironmentSetting.SmtpPort,
        EnvironmentSetting.Email,
        EnvironmentSetting.Password,
        EnvironmentSetting.FrontendBaseUrl
    );

    private static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the email worker.");
    }
}
