using backend.main.utilities.implementation;

using DotNetEnv;

namespace backend.main.configurations.environment
{
    public static class EnvironmentSetting
    {
        private static readonly string _dbConnectionString;
        private static readonly string _redisConnection;
        private static readonly string _rabbitConnection;
        private static readonly string _jwtSecretKeyAccess;
        private static readonly string? _googleCaptchaSecret;
        private static readonly string[] _corsWhiteList;
        private static readonly string? _email;
        private static readonly string? _password;
        private static readonly string? _smtpServer;
        private static readonly string? _microsoftClientId;
        private static readonly string? _microsoftTenantId;
        private static readonly string? _googleClientId;
        private static readonly string? _paypalClientId;
        private static readonly string? _paypalSecretId;
        private static readonly string? _paypalApi;
        private static readonly string _appEnvironment;
        private static readonly string _logLevel;

        static EnvironmentSetting()
        {
            TryLoadEnvFile();

            _dbConnectionString = GetOrDefault(
                "DB_CONNECTION_STRING",
                "Server=localhost;Port=3306;Database=database;User=root;Password=password123"
            );

            _redisConnection = GetOrDefault(
                "REDIS_URL",
                "localhost:6379"
            );

            _rabbitConnection = GetOrDefault(
                "RABBITMQ_URL",
                "amqp://guest:guest@localhost:5672"
            );

            _jwtSecretKeyAccess = GetOrDefault(
                "JWT_SECRET_ACCESS",
                "unit_test_secret_12345678901234567890"
            );

            _googleCaptchaSecret = GetOptional("GOOGLE_CAPTCHA_SECRET");

            _email = GetOptional("EMAIL_USER");
            _password = GetOptional("EMAIL_PASSWORD");
            _smtpServer = GetOptional("SMTP_SERVER");

            _microsoftClientId = GetOptional("MS_CLIENT_ID");
            _microsoftTenantId = GetOptional("MS_TENANT_ID");
            _googleClientId = GetOptional("GOOGLE_CLIENT_ID");

            _appEnvironment = GetOrDefault("ENVIRONMENT", "development").ToLowerInvariant();
            _logLevel = GetOrDefault("LOG_LEVEL", "info").ToLowerInvariant();
        }

        private static void TryLoadEnvFile()
        {
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

            Logger.Debug("No .env file found in directory hierarchy — using system environment variables.");
        }
        private static string? GetOptional(string key)
        {
            var val = Environment.GetEnvironmentVariable(key);

            if (string.IsNullOrWhiteSpace(val))
            {
                Logger.Debug($"Optional environment variable '{key}' not set.");
                return null;
            }

            return val;
        }

        private static string GetOrDefault(string key, string fallback) =>
            GetOptional(key) ?? fallback;

        public static string DbConnectionString => _dbConnectionString;
        public static string RedisConnection => _redisConnection;
        public static string RabbitConnection => _rabbitConnection;
        public static string JwtSecretKeyAccess => _jwtSecretKeyAccess;
        public static string? Email => _email;
        public static string? Password => _password;
        public static string? SmtpServer => _smtpServer;
        public static string? MicrosoftClientId => _microsoftClientId;
        public static string? GoogleClientId => _googleClientId;
        public static string? MicrosoftTenantId => _microsoftTenantId;
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
                { "REDIS_CONNECTION", _redisConnection },
                { "JWT_SECRET_KEY", _jwtSecretKeyAccess }
            };

            var missing = required
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key)
                .ToList();

            if (missing.Any())
                throw new InvalidOperationException(
                    $"Missing required environment variables: {string.Join(", ", missing)}"
                );

            Logger.Info("Environment variables validated successfully.");
        }
    }
}
