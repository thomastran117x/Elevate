using backend.main.application.environment;

namespace backend.main.features.media;

public sealed record MediaValidationStatusConsumerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId)
{
    public static MediaValidationStatusConsumerOptions FromEnvironment() => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.MediaValidationStatusTopic, nameof(EnvironmentSetting.MediaValidationStatusTopic)),
        Require(EnvironmentSetting.MediaValidationStatusGroupId, nameof(EnvironmentSetting.MediaValidationStatusGroupId)));

    private static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for media validation status consumption.");
    }
}
