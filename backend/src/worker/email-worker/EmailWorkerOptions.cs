using backend.main.application.environment;

namespace backend.worker.email_worker;

public sealed record EmailWorkerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic,
    string StatusTopic,
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
        Require(EnvironmentSetting.EmailTopic, nameof(EnvironmentSetting.EmailTopic)),
        Require(EnvironmentSetting.EmailGroupId, nameof(EnvironmentSetting.EmailGroupId)),
        Require(EnvironmentSetting.EmailDlqTopic, nameof(EnvironmentSetting.EmailDlqTopic)),
        EnvironmentSetting.EmailStatusTopic,
        NormalizeSmtpServer(EnvironmentSetting.SmtpServer),
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

    private static string? NormalizeSmtpServer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "gmail" or "google" => "smtp.gmail.com",
            "outlook" or "hotmail" or "live" or "office365" => "smtp.office365.com",
            "yahoo" => "smtp.mail.yahoo.com",
            _ => normalized
        };
    }
}
