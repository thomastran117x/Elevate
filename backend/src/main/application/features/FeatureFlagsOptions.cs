using Microsoft.Extensions.Configuration;

namespace backend.main.application.features;

public sealed class FeatureFlagsOptions
{
    public Dictionary<string, bool> Flags { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static FeatureFlagsOptions FromConfiguration(
        IConfiguration configuration,
        FeatureFlagRegistry registry)
    {
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var section = configuration.GetSection("FeatureFlags");

        foreach (var child in section.GetChildren())
        {
            registry.EnsureKnown(child.Key);
            flags[registry.Normalize(child.Key)] = ParseBoolean(child.Value, child.Path);
        }

        foreach (var key in registry.Keys)
        {
            var envVarName = registry.ToEnvironmentVariableName(key);
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            if (string.IsNullOrWhiteSpace(envValue))
                continue;

            flags[registry.Normalize(key)] = ParseBoolean(envValue, envVarName);
        }

        return new FeatureFlagsOptions
        {
            Flags = flags
        };
    }

    private static bool ParseBoolean(string? rawValue, string sourceName)
    {
        if (bool.TryParse(rawValue, out var value))
            return value;

        throw new InvalidOperationException(
            $"Feature flag value '{rawValue}' from '{sourceName}' must be 'true' or 'false'.");
    }
}
