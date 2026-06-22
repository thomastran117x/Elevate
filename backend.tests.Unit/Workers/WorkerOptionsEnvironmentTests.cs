using System.Reflection;
using System.Runtime.Loader;

using FluentAssertions;

using backend.tests.Unit.Support;

namespace backend.tests.Unit.Workers;

[Collection(EnvironmentVariableTestCollection.Name)]
public class WorkerOptionsEnvironmentTests
{
    [Fact]
    public void SmsWorkerOptions_FromEnvironment_ShouldReadConfiguredValues()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["KAFKA_BOOTSTRAP_SERVERS"] = "kafka:9092",
            ["SMS_TOPIC"] = "custom-sms",
            ["SMS_GROUP_ID"] = "sms-group",
            ["SMS_DLQ_TOPIC"] = "sms-dlq",
            ["TWILIO_ACCOUNT_SID"] = "sid",
            ["TWILIO_AUTH_TOKEN"] = "token",
            ["TWILIO_MESSAGING_SERVICE_SID"] = "mg-service",
            ["TWILIO_FROM_PHONE_NUMBER"] = "+14165550123"
        });

        using var harness = AssemblyOptionsHarness.Load("sms-worker.dll", "backend.worker.sms_worker.SmsWorkerOptions");
        var options = harness.InvokeFromEnvironment();

        harness.GetString(options, "BootstrapServers").Should().Be("kafka:9092");
        harness.GetString(options, "Topic").Should().Be("custom-sms");
        harness.GetString(options, "GroupId").Should().Be("sms-group");
        harness.GetString(options, "DlqTopic").Should().Be("sms-dlq");
        harness.GetNullableString(options, "AccountSid").Should().Be("sid");
        harness.GetNullableString(options, "AuthToken").Should().Be("token");
        harness.GetNullableString(options, "MessagingServiceSid").Should().Be("mg-service");
        harness.GetNullableString(options, "FromPhoneNumber").Should().Be("+14165550123");
    }

    [Fact]
    public void SmsWorkerOptions_FromEnvironment_ShouldThrowWhenTopicIsMissing()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["KAFKA_BOOTSTRAP_SERVERS"] = "kafka:9092",
            ["SMS_TOPIC"] = null,
            ["SMS_GROUP_ID"] = "sms-group",
            ["SMS_DLQ_TOPIC"] = "sms-dlq"
        });

        using var harness = AssemblyOptionsHarness.Load("sms-worker.dll", "backend.worker.sms_worker.SmsWorkerOptions");

        harness.Invoking(h => h.InvokeFromEnvironment())
            .Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*SmsTopic must be configured*");
    }

    [Fact]
    public void EmailWorkerOptions_FromEnvironment_ShouldReadConfiguredValues()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["KAFKA_BOOTSTRAP_SERVERS"] = "kafka:9092",
            ["EMAIL_TOPIC"] = "custom-email",
            ["EMAIL_GROUP_ID"] = "email-group",
            ["EMAIL_DLQ_TOPIC"] = "email-dlq",
            ["EMAIL_STATUS_TOPIC"] = "email-status",
            ["SMTP_SERVER"] = "smtp.test",
            ["SMTP_PORT"] = "2525",
            ["EMAIL_USER"] = "mailer@example.com",
            ["EMAIL_PASSWORD"] = "password123",
            ["FRONTEND_URL"] = "https://frontend.test"
        });

        using var harness = AssemblyOptionsHarness.Load("email-worker.dll", "backend.worker.email_worker.EmailWorkerOptions");
        var options = harness.InvokeFromEnvironment();

        harness.GetString(options, "BootstrapServers").Should().Be("kafka:9092");
        harness.GetString(options, "Topic").Should().Be("custom-email");
        harness.GetString(options, "GroupId").Should().Be("email-group");
        harness.GetString(options, "DlqTopic").Should().Be("email-dlq");
        harness.GetString(options, "StatusTopic").Should().Be("email-status");
        harness.GetNullableString(options, "SmtpServer").Should().Be("smtp.test");
        harness.GetInt32(options, "SmtpPort").Should().Be(2525);
        harness.GetNullableString(options, "Username").Should().Be("mailer@example.com");
        harness.GetNullableString(options, "Password").Should().Be("password123");
        harness.GetString(options, "FrontendBaseUrl").Should().Be("https://frontend.test");
    }

    [Fact]
    public void EmailWorkerOptions_FromEnvironment_ShouldThrowWhenTopicIsMissing()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["KAFKA_BOOTSTRAP_SERVERS"] = "kafka:9092",
            ["EMAIL_TOPIC"] = null,
            ["EMAIL_GROUP_ID"] = "email-group",
            ["EMAIL_DLQ_TOPIC"] = "email-dlq"
        });

        using var harness = AssemblyOptionsHarness.Load("email-worker.dll", "backend.worker.email_worker.EmailWorkerOptions");

        harness.Invoking(h => h.InvokeFromEnvironment())
            .Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*EmailTopic must be configured*");
    }

    private sealed class AssemblyOptionsHarness : IDisposable
    {
        private readonly AssemblyLoadContext _loadContext;
        private readonly Type _type;

        private AssemblyOptionsHarness(AssemblyLoadContext loadContext, Type type)
        {
            _loadContext = loadContext;
            _type = type;
        }

        public static AssemblyOptionsHarness Load(string assemblyFileName, string typeName)
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
            var loadContext = new IsolatedAssemblyLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(typeName, throwOnError: true)!;
            return new AssemblyOptionsHarness(loadContext, type);
        }

        public object InvokeFromEnvironment() =>
            _type.GetMethod("FromEnvironment", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

        public string GetString(object instance, string propertyName) =>
            (string)instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;

        public string? GetNullableString(object instance, string propertyName) =>
            (string?)instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance);

        public int GetInt32(object instance, string propertyName) =>
            (int)instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;

        public void Dispose()
        {
            _loadContext.Unload();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class IsolatedAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public IsolatedAssemblyLoadContext(string mainAssemblyPath)
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
