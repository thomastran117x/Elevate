using backend.main.utilities.implementation;

using DotNetEnv;

namespace backend.main.application.environment
{
    public static class EnvironmentSetting
    {
        private static readonly bool _runningInContainer;
        private static readonly string _dbConnectionString;
        private static readonly string _redisConnection;
        private static readonly string _jwtSecretKeyAccess;
        private static readonly string _jwtSecretKeyVerification;
        private static readonly string? _googleCaptchaSecret;
        private static readonly string[] _corsWhiteList;
        private static readonly string? _email;
        private static readonly string? _password;
        private static readonly string? _smtpServer;
        private static readonly int _smtpPort;
        private static readonly string _frontendBaseUrl;
        private static readonly string? _microsoftClientId;
        private static readonly string? _microsoftTenantId;
        private static readonly string? _googleClientId;
        private static readonly string? _googleClientSecret;
        private static readonly string? _appleClientId;
        private static readonly string? _paypalClientId;
        private static readonly string? _paypalSecretId;
        private static readonly string? _paypalApi;
        private static readonly string? _azureStorageConnectionString;
        private static readonly string? _azureStorageContainerName;
        private static readonly string? _elasticsearchUrl;
        private static readonly string _kafkaBootstrapServers;
        private static readonly string _eventIndexTopic;
        private static readonly string _eventIndexGroupId;
        private static readonly string _eventIndexDlqTopic;
        private static readonly string _appEnvironment;
        private static readonly string _logLevel;
        private const string DefaultJwtSecretAccess = "unit_test_secret_12345678901234567890";
        private const string DefaultJwtSecretVerification =
            "unit_test_verification_secret_12345678901234567890";

        static EnvironmentSetting()
        {
            _runningInContainer = IsRunningInContainer();
            TryLoadEnvFile();

            _dbConnectionString = GetOrDefault(
                ["DB_CONNECTION_STRING"],
                "Server=localhost;Port=3306;Database=database;User=root;Password=password123"
            );

            _redisConnection = GetOrDefault(
                ["REDIS_URL", "REDIS_CONNECTION"],
                "localhost:6379"
            );

            _jwtSecretKeyAccess = GetOrDefault(
                ["JWT_SECRET_ACCESS", "JWT_SECRET_KEY"],
                DefaultJwtSecretAccess
            );

            _jwtSecretKeyVerification = GetOrDefault(
                ["JWT_SECRET_VERIFICATION"],
                DefaultJwtSecretVerification
            );

            _googleCaptchaSecret = GetOptional(["GOOGLE_CAPTCHA_SECRET"]);

            _email = GetOptional(["EMAIL_USER"]);
            _password = GetOptional(["EMAIL_PASSWORD"]);
            _smtpServer = GetOptional(["SMTP_SERVER"]);
            _smtpPort = GetOptional(["SMTP_PORT"]) is string smtpPortText
                && int.TryParse(smtpPortText, out var smtpPort)
                ? smtpPort
                : 587;
            _frontendBaseUrl = GetOrDefault(
                ["Frontend:BaseUrl", "FRONTEND_URL"],
                "http://localhost:3090"
            );

            _microsoftClientId = GetOptional(["MS_CLIENT_ID"]);
            _microsoftTenantId = GetOptional(["MS_TENANT_ID"]);
            _googleClientId = GetOptional(["GOOGLE_CLIENT_ID"]);
            _googleClientSecret = GetOptional(["GOOGLE_CLIENT_SECRET"]);
            _appleClientId = GetOptional(["APPLE_CLIENT_ID"]);

            _azureStorageConnectionString = GetOptional(["AZURE_STORAGE_CONNECTION_STRING"]);
            _azureStorageContainerName = GetOptional(["AZURE_STORAGE_CONTAINER_NAME"]);

            _elasticsearchUrl = GetOptional(["ELASTICSEARCH_URL"]);
            _kafkaBootstrapServers = GetOrDefault(
                ["KAFKA_BOOTSTRAP_SERVERS"],
                "localhost:9092"
            );
            _eventIndexTopic = GetOrDefault(
                ["EVENT_INDEX_TOPIC"],
                "event-index-events"
            );
            _eventIndexGroupId = GetOrDefault(
                ["EVENT_INDEX_GROUP_ID"],
                "event-indexer"
            );
            _eventIndexDlqTopic = GetOrDefault(
                ["EVENT_INDEX_DLQ_TOPIC"],
                "event-index-events-dlq"
            );

            _appEnvironment = (
                GetOptional(["ENVIRONMENT", "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT"])
                ?? "development"
            ).ToLowerInvariant();
            _logLevel = GetOrDefault(["LOG_LEVEL"], "info").ToLowerInvariant();
        }

        private static void TryLoadEnvFile()
        {
            if (_runningInContainer)
            {
                Logger.Info(
                    "Running in container; skipping .env file discovery and using injected environment variables."
                );
                return;
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);

            while (dir != null)
            {
                var envPath = Path.Combine(dir.FullName, ".env");

                if (File.Exists(envPath))
                {
                    try
                    {
                        Env.Load(envPath);
                        Logger.Info($".env file loaded from: {envPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"Failed to load .env file at {envPath}");
                    }
                    return;
                }

                dir = dir.Parent;
            }

            Logger.Debug("No .env file found in directory hierarchy; using system environment variables.");
        }

        private static string? GetOptional(params string[] keys)
        {
            foreach (var key in keys)
            {
                var val = Environment.GetEnvironmentVariable(key);

                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }

            Logger.Debug(
                $"Optional environment variable(s) not set: {string.Join(", ", keys)}."
            );
            return null;
        }

        private static string GetOrDefault(string[] keys, string fallback) =>
            GetOptional(keys) ?? fallback;

        private static bool IsRunningInContainer()
        {
            return bool.TryParse(
                    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                    out var running
                ) && running;
        }

        public static string DbConnectionString => _dbConnectionString;
        public static string RedisConnection => _redisConnection;
        public static string JwtSecretKeyAccess => _jwtSecretKeyAccess;
        public static string JwtSecretKeyVerification => _jwtSecretKeyVerification;
        public static string? Email => _email;
        public static string? Password => _password;
        public static string? SmtpServer => _smtpServer;
        public static int SmtpPort => _smtpPort;
        public static string FrontendBaseUrl => _frontendBaseUrl;
        public static string? MicrosoftClientId => _microsoftClientId;
        public static string? GoogleClientId => _googleClientId;
        public static string? GoogleClientSecret => _googleClientSecret;
        public static string? AppleClientId => _appleClientId;
        public static string? MicrosoftTenantId => _microsoftTenantId;
        public static string? AzureStorageConnectionString => _azureStorageConnectionString;
        public static string? AzureStorageContainerName => _azureStorageContainerName;
        public static string? ElasticsearchUrl => _elasticsearchUrl;
        public static string KafkaBootstrapServers => _kafkaBootstrapServers;
        public static string EventIndexTopic => _eventIndexTopic;
        public static string EventIndexGroupId => _eventIndexGroupId;
        public static string EventIndexDlqTopic => _eventIndexDlqTopic;
        public static string AppEnvironment => _appEnvironment;
        public static string LogLevel => _logLevel;

        public static void Validate()
        {
            if (_appEnvironment is "development" or "test")
            {
                Logger.Warn("Skipping environment validation (dev/test mode).");
                return;
            }

            var required = new Dictionary<string, string>
            {
                { "DB_CONNECTION_STRING", _dbConnectionString },
                { "REDIS_URL", _redisConnection },
                { "JWT_SECRET_ACCESS", _jwtSecretKeyAccess },
                { "JWT_SECRET_VERIFICATION", _jwtSecretKeyVerification },
                { "AZURE_STORAGE_CONNECTION_STRING", _azureStorageConnectionString ?? "" },
                { "AZURE_STORAGE_CONTAINER_NAME", _azureStorageContainerName ?? "" }
            };

            var missing = required
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key)
                .ToList();

            if (missing.Any())
                throw new InvalidOperationException(
                    $"Missing required environment variables: {string.Join(", ", missing)}"
                );

            if (
                _jwtSecretKeyAccess == DefaultJwtSecretAccess
                || _jwtSecretKeyVerification == DefaultJwtSecretVerification
            )
            {
                throw new InvalidOperationException(
                    "Production JWT secrets must be configured and cannot use fallback values."
                );
            }

            Logger.Info("Environment variables validated successfully.");
        }
    }
}
