using backend.main.application.environment;
using backend.main.features.auth.events;
using backend.main.features.auth.history;

using Microsoft.Extensions.Configuration;

namespace backend.worker.security_history_worker;

/// <summary>
/// Settings for the security-history worker, read once at startup. Anything out of range stops
/// the worker from starting rather than silently keeping history for the wrong length of time or
/// alerting on the wrong changes.
/// </summary>
public sealed record SecurityHistoryWorkerOptions(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string DlqTopic,
    int RetentionDays,
    int PurgeBatchSize,
    int PurgeMaxBatches,
    TimeSpan PurgeInterval,
    bool NotificationsEnabled,
    IReadOnlySet<AccountSecurityEventKind> AlertKinds,
    TimeSpan AlertMaxAge)
{
    public const string RetentionDaysKey = "SECURITY_HISTORY_RETENTION_DAYS";
    public const string PurgeBatchSizeKey = "SECURITY_HISTORY_PURGE_BATCH_SIZE";
    public const string PurgeMaxBatchesKey = "SECURITY_HISTORY_PURGE_MAX_BATCHES";
    public const string PurgeIntervalMinutesKey = "SECURITY_HISTORY_PURGE_INTERVAL_MINUTES";
    public const string NotificationsEnabledKey = "SECURITY_HISTORY_NOTIFICATIONS_ENABLED";
    public const string AlertKindsKey = "SECURITY_HISTORY_ALERT_KINDS";
    public const string AlertMaxAgeMinutesKey = "SECURITY_HISTORY_ALERT_MAX_AGE_MINUTES";

    /// <summary>
    /// How old a change may be and still be alerted about. Past it, a backlog (a worker that was
    /// down, alerts turned back on) would email owners about changes long gone.
    /// </summary>
    public static readonly TimeSpan DefaultAlertMaxAge = TimeSpan.FromHours(24);

    /// <summary>
    /// The longest the history may be kept, and the default. The API and the Activity tab promise
    /// owners that entries are kept for up to 90 days, so the setting can shorten the window but
    /// never extend it past what they were told.
    /// </summary>
    public const int MaxRetentionDays = 90;

    /// <summary>
    /// The changes that send nothing of their own today. MFA, email, and password-reset changes
    /// already notify the owner directly when they happen, so alerting on them too would send two
    /// emails for one change.
    /// </summary>
    public static readonly IReadOnlySet<AccountSecurityEventKind> DefaultAlertKinds =
        new HashSet<AccountSecurityEventKind>
        {
            AccountSecurityEventKind.PasswordChanged,
            AccountSecurityEventKind.ProviderLinked,
            AccountSecurityEventKind.RefreshTokenReuseDetected,
            AccountSecurityEventKind.AccountDisabled,
        };

    public static SecurityHistoryWorkerOptions FromConfiguration(IConfiguration configuration) => new(
        Require(EnvironmentSetting.KafkaBootstrapServers, nameof(EnvironmentSetting.KafkaBootstrapServers)),
        Require(EnvironmentSetting.SecurityHistoryTopic, nameof(EnvironmentSetting.SecurityHistoryTopic)),
        Require(EnvironmentSetting.SecurityHistoryGroupId, nameof(EnvironmentSetting.SecurityHistoryGroupId)),
        Require(EnvironmentSetting.SecurityHistoryDlqTopic, nameof(EnvironmentSetting.SecurityHistoryDlqTopic)),
        ReadInt(configuration, RetentionDaysKey, MaxRetentionDays, 1, MaxRetentionDays),
        ReadInt(configuration, PurgeBatchSizeKey, 500, 1, 5000),
        ReadInt(configuration, PurgeMaxBatchesKey, 20, 1, 1000),
        TimeSpan.FromMinutes(ReadInt(configuration, PurgeIntervalMinutesKey, 60, 1, 1440)),
        ReadBool(configuration, NotificationsEnabledKey, true),
        ReadAlertKinds(configuration[AlertKindsKey]),
        TimeSpan.FromMinutes(ReadInt(configuration, AlertMaxAgeMinutesKey, (int)DefaultAlertMaxAge.TotalMinutes, 5, 10_080)));

    internal static string Require(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException($"{settingName} must be configured for the security-history worker.");
    }

    internal static int ReadInt(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (int.TryParse(raw.Trim(), out var value) && value >= min && value <= max)
            return value;

        throw new InvalidOperationException($"{key} must be a whole number from {min} to {max}.");
    }

    internal static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (bool.TryParse(raw.Trim(), out var value))
            return value;

        throw new InvalidOperationException($"{key} must be 'true' or 'false'.");
    }

    /// <summary>
    /// A comma-separated list of snake_case kinds, e.g. <c>password_changed,provider_linked</c>.
    /// Unset means the defaults; <c>none</c> means no alerts at all; an unknown name fails.
    /// </summary>
    internal static IReadOnlySet<AccountSecurityEventKind> ReadAlertKinds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultAlertKinds;

        if (string.Equals(raw.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            return new HashSet<AccountSecurityEventKind>();

        var byName = Enum.GetValues<AccountSecurityEventKind>()
            .ToDictionary(kind => AuthSecurityWireNames.Of(kind), StringComparer.OrdinalIgnoreCase);
        var kinds = new HashSet<AccountSecurityEventKind>();
        foreach (var name in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!byName.TryGetValue(name, out var kind))
                throw new InvalidOperationException($"{AlertKindsKey} names an unknown kind '{name}'.");
            kinds.Add(kind);
        }

        // Only "none" turns alerts off. A value of nothing but separators is a malformed setting,
        // and reading it as "no alerts" would silently stop every high-risk notice.
        if (kinds.Count == 0)
            throw new InvalidOperationException($"{AlertKindsKey} names no kinds; leave it unset for the defaults or use 'none'.");

        return kinds;
    }
}
