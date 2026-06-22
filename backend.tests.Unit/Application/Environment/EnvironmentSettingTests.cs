using System.Reflection;
using System.Runtime.Loader;

using FluentAssertions;

using backend.tests.Unit.Support;

namespace backend.tests.Unit.Application.EnvironmentConfig;

[Collection(EnvironmentVariableTestCollection.Name)]
public class EnvironmentSettingTests
{
    [Fact]
    public void Defaults_ShouldBeUsed_WhenEnvironmentVariablesAreMissing()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["DB_CONNECTION_STRING"] = null,
            ["REDIS_URL"] = null,
            ["REDIS_CONNECTION"] = null,
            ["JWT_SECRET_ACCESS"] = null,
            ["JWT_SECRET_KEY"] = null,
            ["JWT_SECRET_VERIFICATION"] = null,
            ["FRONTEND_URL"] = null,
            ["ENVIRONMENT"] = null,
            ["ASPNETCORE_ENVIRONMENT"] = null,
            ["DOTNET_ENVIRONMENT"] = null,
            ["LOG_LEVEL"] = null
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.GetString("DbConnectionString")
            .Should().Be("Server=localhost;Port=3306;Database=database;User=root;Password=password123");
        harness.GetString("RedisConnection").Should().Be("localhost:6379");
        harness.GetString("JwtSecretKeyAccess").Should().Be("unit_test_secret_12345678901234567890");
        harness.GetString("JwtSecretKeyVerification").Should().Be("unit_test_verification_secret_12345678901234567890");
        harness.GetString("FrontendBaseUrl").Should().Be("http://localhost:3090");
        harness.GetString("AppEnvironment").Should().Be("development");
        harness.GetString("LogLevel").Should().Be("info");
    }

    [Fact]
    public void AlternateEnvironmentVariables_ShouldBeRespected()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["REDIS_URL"] = null,
            ["REDIS_CONNECTION"] = "redis:6380",
            ["JWT_SECRET_ACCESS"] = null,
            ["JWT_SECRET_KEY"] = "fallback-access-secret",
            ["FRONTEND_URL"] = "https://frontend.test",
            ["MS_CLIENT_ID"] = "ms-client",
            ["ELASTICSEARCH_URL"] = "https://es.test",
            ["KAFKA_BOOTSTRAP_SERVERS"] = "kafka:9092",
            ["EVENT_INDEX_TOPIC"] = "events-topic",
            ["EMAIL_STATUS_GROUP_ID"] = "email-status-group",
            ["LOG_LEVEL"] = "DEBUG"
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.GetString("RedisConnection").Should().Be("redis:6380");
        harness.GetString("JwtSecretKeyAccess").Should().Be("fallback-access-secret");
        harness.GetString("FrontendBaseUrl").Should().Be("https://frontend.test");
        harness.GetNullableString("MicrosoftClientId").Should().Be("ms-client");
        harness.GetNullableString("ElasticsearchUrl").Should().Be("https://es.test");
        harness.GetString("KafkaBootstrapServers").Should().Be("kafka:9092");
        harness.GetString("EventIndexTopic").Should().Be("events-topic");
        harness.GetString("EmailStatusGroupId").Should().Be("email-status-group");
        harness.GetString("LogLevel").Should().Be("debug");
    }

    [Fact]
    public void NotificationTopics_ShouldFallBackToDefaults_WhenTopicEnvironmentVariablesAreMissing()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["EMAIL_TOPIC"] = null,
            ["SMS_TOPIC"] = null
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.GetString("EmailTopic").Should().Be("eventxperience-email");
        harness.GetString("SmsTopic").Should().Be("eventxperience-sms");
        harness.GetStringFromType("backend.main.shared.providers.messages.NotificationTopics", "Email")
            .Should().Be("eventxperience-email");
        harness.GetStringFromType("backend.main.shared.providers.messages.NotificationTopics", "Sms")
            .Should().Be("eventxperience-sms");
    }

    [Fact]
    public void NotificationTopics_ShouldUseConfiguredTopicNames_WhenEnvironmentVariablesArePresent()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["EMAIL_TOPIC"] = "custom-email-topic",
            ["SMS_TOPIC"] = "custom-sms-topic"
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.GetString("EmailTopic").Should().Be("custom-email-topic");
        harness.GetString("SmsTopic").Should().Be("custom-sms-topic");
        harness.GetStringFromType("backend.main.shared.providers.messages.NotificationTopics", "Email")
            .Should().Be("custom-email-topic");
        harness.GetStringFromType("backend.main.shared.providers.messages.NotificationTopics", "Sms")
            .Should().Be("custom-sms-topic");
    }

    [Fact]
    public void Validate_ShouldSkipInTestEnvironment()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["ENVIRONMENT"] = "test"
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.Invoking(h => h.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrowWhenRequiredProductionValuesAreMissing()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["ENVIRONMENT"] = "production",
            ["JWT_SECRET_ACCESS"] = "production-access-secret-1234567890",
            ["JWT_SECRET_VERIFICATION"] = "production-verification-secret-1234567890",
            ["AZURE_STORAGE_CONNECTION_STRING"] = null,
            ["AZURE_STORAGE_CONTAINER_NAME"] = null
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.Invoking(h => h.Validate())
            .Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*Missing required environment variables: AZURE_STORAGE_CONNECTION_STRING, AZURE_STORAGE_CONTAINER_NAME*");
    }

    [Fact]
    public void Validate_ShouldThrowWhenProductionJwtSecretsUseFallbackValues()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["ENVIRONMENT"] = "production",
            ["AZURE_STORAGE_CONNECTION_STRING"] = "UseDevelopmentStorage=true",
            ["AZURE_STORAGE_CONTAINER_NAME"] = "images",
            ["JWT_SECRET_ACCESS"] = null,
            ["JWT_SECRET_KEY"] = null,
            ["JWT_SECRET_VERIFICATION"] = null
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.Invoking(h => h.Validate())
            .Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*Production JWT secrets must be configured and cannot use fallback values.*");
    }

    [Fact]
    public void Validate_ShouldSucceedWhenProductionConfigurationIsComplete()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["ENVIRONMENT"] = "production",
            ["DB_CONNECTION_STRING"] = "Server=db;Database=prod;",
            ["REDIS_URL"] = "redis:6379",
            ["JWT_SECRET_ACCESS"] = "production-access-secret-1234567890",
            ["JWT_SECRET_VERIFICATION"] = "production-verification-secret-1234567890",
            ["AZURE_STORAGE_CONNECTION_STRING"] = "UseDevelopmentStorage=true",
            ["AZURE_STORAGE_CONTAINER_NAME"] = "images"
        });

        using var harness = EnvironmentSettingHarness.Load();

        harness.Invoking(h => h.Validate()).Should().NotThrow();
    }

    private sealed class EnvironmentSettingHarness : IDisposable
    {
        private readonly AssemblyLoadContext _loadContext;
        private readonly Type _type;
        private readonly Assembly _assembly;

        private EnvironmentSettingHarness(AssemblyLoadContext loadContext, Assembly assembly, Type type)
        {
            _loadContext = loadContext;
            _assembly = assembly;
            _type = type;
        }

        public static EnvironmentSettingHarness Load()
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "backend.dll");
            var loadContext = new IsolatedBackendLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType("backend.main.application.environment.EnvironmentSetting", throwOnError: true)!;
            return new EnvironmentSettingHarness(loadContext, assembly, type);
        }

        public string GetString(string propertyName) =>
            (string)_type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        public string? GetNullableString(string propertyName) =>
            (string?)_type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null);

        public string GetStringFromType(string typeName, string propertyName)
        {
            var targetType = _assembly.GetType(typeName, throwOnError: true)!;
            return (string)targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        }

        public void Validate() =>
            _type.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

        public void Dispose()
        {
            _loadContext.Unload();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class IsolatedBackendLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public IsolatedBackendLoadContext(string mainAssemblyPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                _originals[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _originals)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}


