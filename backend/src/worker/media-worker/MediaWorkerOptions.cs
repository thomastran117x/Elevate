using backend.main.application.environment;

namespace backend.worker.media_worker;

public sealed record MediaWorkerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic,
    string StatusTopic,
    string? AzureStorageConnectionString,
    string? PublicContainerName,
    string? QuarantineContainerName)
{
    /// <summary>
    /// Whether there is storage to validate against. Compose runs the worker whether or not the
    /// developer has an Azure account configured, so its absence idles the worker rather than
    /// crash-looping it.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AzureStorageConnectionString)
        && !string.IsNullOrWhiteSpace(PublicContainerName)
        && !string.IsNullOrWhiteSpace(QuarantineContainerName);

    public static MediaWorkerOptions FromEnvironment() => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.MediaValidationTopic, nameof(EnvironmentSetting.MediaValidationTopic)),
        Require(EnvironmentSetting.MediaValidationGroupId, nameof(EnvironmentSetting.MediaValidationGroupId)),
        Require(EnvironmentSetting.MediaValidationDlqTopic, nameof(EnvironmentSetting.MediaValidationDlqTopic)),
        Require(EnvironmentSetting.MediaValidationStatusTopic, nameof(EnvironmentSetting.MediaValidationStatusTopic)),
        EnvironmentSetting.AzureStorageConnectionString,
        EnvironmentSetting.AzureStorageContainerName,
        EnvironmentSetting.AzureStorageQuarantineContainerName
    );

    internal static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the media worker.");
    }
}
