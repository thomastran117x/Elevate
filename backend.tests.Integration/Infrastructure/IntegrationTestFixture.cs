using System.Data.Common;
using System.Net;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.EntityFrameworkCore;

using StackExchange.Redis;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

[assembly: CollectionBehavior(MaxParallelThreads = 4)]

namespace backend.tests.Integration.Infrastructure;

public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object ShutdownGate = new();
    private static IntegrationTestEnvironment? _environment;
    private static Task? _shutdownTask;

    public static async Task<IntegrationTestEnvironment> GetEnvironmentAsync()
    {
        if (_environment is not null)
            return _environment;

        await Gate.WaitAsync();
        try
        {
            if (_environment is not null)
                return _environment;

            _environment = await IntegrationTestEnvironment.CreateAsync();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            return _environment;
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task InitializeAsync() => GetEnvironmentAsync();

    public Task DisposeAsync() => ShutdownAsync();

    private static void Shutdown()
    {
        try
        {
            ShutdownAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Process shutdown is best-effort. Testcontainers' resource reaper is the
            // fallback when the host can no longer complete asynchronous cleanup.
        }
    }

    private static Task ShutdownAsync()
    {
        lock (ShutdownGate)
            return _shutdownTask ??= ShutdownCoreAsync();
    }

    private static async Task ShutdownCoreAsync()
    {
        try
        {
            await AuthApiTestAppPool.DisposeIfCreatedAsync();
        }
        finally
        {
            var environment = Interlocked.Exchange(ref _environment, null);
            if (environment is not null)
                await environment.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    public const string Name = "backend.tests.Integration";
}

public sealed class IntegrationTestEnvironment : IAsyncDisposable
{
    private const string PostgresImage = "postgres:17-alpine";
    private const string ElasticsearchImage = "docker.elastic.co/elasticsearch/elasticsearch:8.16.1";
    private const string PostgresUser = "postgres";
    private const string PostgresPassword = "postgres";
    private const string DefaultDatabase = "appdb";
    private const string EmailTopicName = "eventxperience-email";
    private const string SmsTopicName = "eventxperience-sms";
    private const string EmailStatusTopicName = "eventxperience-email-status";

    private readonly RedisContainer _redisContainer;
    private readonly KafkaContainer _kafkaContainer;
    private readonly PostgreSqlContainer _postgresContainer;
    private readonly IContainer _elasticsearchContainer;
    private int _disposed;

    private IntegrationTestEnvironment(
        PostgreSqlContainer postgresContainer,
        RedisContainer redisContainer,
        KafkaContainer kafkaContainer,
        IContainer elasticsearchContainer)
    {
        _postgresContainer = postgresContainer;
        _redisContainer = redisContainer;
        _kafkaContainer = kafkaContainer;
        _elasticsearchContainer = elasticsearchContainer;
    }

    public string PostgresServerConnectionString { get; private set; } = string.Empty;

    public string RedisConnectionString { get; private set; } = string.Empty;

    public string KafkaBootstrapServers { get; private set; } = string.Empty;

    public string ElasticsearchUrl { get; private set; } = string.Empty;

    public string EmailTopic => EmailTopicName;

    public string SmsTopic => SmsTopicName;

    public string EmailStatusTopic => EmailStatusTopicName;

    public static async Task<IntegrationTestEnvironment> CreateAsync()
    {
        // PostgreSqlBuilder supplies a pg_isready wait strategy, which replaces the
        // hand-rolled readiness polling the MySQL container needed.
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage(PostgresImage)
            .WithCleanUp(true)
            .WithDatabase(DefaultDatabase)
            .WithUsername(PostgresUser)
            .WithPassword(PostgresPassword)
            .Build();

        var redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithCleanUp(true)
            .Build();

        var kafkaContainer = new KafkaBuilder()
            .WithCleanUp(true)
            .Build();

        var elasticsearchContainer = new ContainerBuilder()
            .WithImage(ElasticsearchImage)
            .WithCleanUp(true)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("xpack.security.http.ssl.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithPortBinding(9200, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                    .ForPort(9200)
                    .ForPath("/_cluster/health")))
            .Build();

        var environment = new IntegrationTestEnvironment(
            postgresContainer,
            redisContainer,
            kafkaContainer,
            elasticsearchContainer);

        try
        {
            await environment.StartAsync();
            return environment;
        }
        catch
        {
            try
            {
                await environment.DisposeAsync();
            }
            catch
            {
                // Preserve the startup exception. Resource-reaper cleanup remains
                // enabled for any container Docker could not remove immediately.
            }

            throw;
        }
    }

    public string CreateDatabaseConnectionString(string databaseName)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = PostgresServerConnectionString
        };
        builder["Database"] = databaseName;
        return builder.ConnectionString;
    }

    public string CreateRedisConnectionString(int database)
    {
        var options = ConfigurationOptions.Parse(RedisConnectionString);
        options.DefaultDatabase = database;
        return options.ToString();
    }

    public KafkaTopicProbe CreateKafkaProbe() => new(KafkaBootstrapServers);

    public async Task ResetSharedStateAsync(
        int redisDatabase,
        params string[] elasticsearchIndices)
    {
        await Task.WhenAll(
            [
                FlushRedisAsync(redisDatabase),
                .. elasticsearchIndices.Select(ClearElasticsearchIndexAsync)
            ]);
    }

    public Task EnsureKafkaTopicsExistAsync(params string[] topicNames) =>
        EnsureKafkaTopicsCoreAsync(topicNames);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await Task.WhenAll(
            _elasticsearchContainer.DisposeAsync().AsTask(),
            _kafkaContainer.DisposeAsync().AsTask(),
            _redisContainer.DisposeAsync().AsTask(),
            _postgresContainer.DisposeAsync().AsTask());
    }

    private async Task StartAsync()
    {
        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _redisContainer.StartAsync(),
            _kafkaContainer.StartAsync(),
            _elasticsearchContainer.StartAsync());

        PostgresServerConnectionString = BuildPostgresConnectionString(DefaultDatabase);
        RedisConnectionString = BuildRedisConnectionString();
        KafkaBootstrapServers = _kafkaContainer.GetBootstrapAddress();
        ElasticsearchUrl =
            $"http://{_elasticsearchContainer.Hostname}:{_elasticsearchContainer.GetMappedPublicPort(9200)}";

        SetEnvironmentVariables();
        await PostgresTestDatabase.InitializeTemplateAsync(this);
        await EnsureKafkaTopicsCoreAsync([EmailTopicName, SmsTopicName, EmailStatusTopicName]);
    }

    private string BuildPostgresConnectionString(string databaseName) =>
        string.Join(
            ';',
            [
                $"Host={_postgresContainer.Hostname}",
                $"Port={_postgresContainer.GetMappedPublicPort(5432)}",
                $"Username={PostgresUser}",
                $"Password={PostgresPassword}",
                $"Database={databaseName}",
                "SSL Mode=Disable",
                "Pooling=False"
            ]);

    private string BuildRedisConnectionString()
    {
        var options = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        options.AllowAdmin = true;
        return options.ToString();
    }

    private void SetEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", PostgresServerConnectionString);
        Environment.SetEnvironmentVariable("REDIS_URL", RedisConnectionString);
        Environment.SetEnvironmentVariable("ELASTICSEARCH_URL", ElasticsearchUrl);
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", KafkaBootstrapServers);
        Environment.SetEnvironmentVariable("EMAIL_TOPIC", EmailTopicName);
        Environment.SetEnvironmentVariable("SMS_TOPIC", SmsTopicName);
        Environment.SetEnvironmentVariable("EMAIL_STATUS_TOPIC", EmailStatusTopicName);
    }

    private async Task FlushRedisAsync(int database)
    {
        using var mux = await ConnectionMultiplexer.ConnectAsync(CreateRedisConnectionString(database));
        var server = mux.GetServer(mux.GetEndPoints().First());
        await server.FlushDatabaseAsync(database);
    }

    private async Task ClearElasticsearchIndexAsync(string indexName)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(ElasticsearchUrl)
        };

        using var response = await httpClient.PostAsync(
            $"/{indexName}/_delete_by_query?refresh=true&conflicts=proceed",
            new StringContent("{\"query\":{\"match_all\":{}}}", System.Text.Encoding.UTF8, "application/json"));
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        response.EnsureSuccessStatusCode();
    }

    private async Task EnsureKafkaTopicsCoreAsync(IReadOnlyCollection<string> topicNames)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = KafkaBootstrapServers
        }).Build();

        try
        {
            await admin.CreateTopicsAsync(
                topicNames.Select(CreateTopicSpecification));
        }
        catch (CreateTopicsException ex)
            when (ex.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    private static TopicSpecification CreateTopicSpecification(string name) =>
        new()
        {
            Name = name,
            NumPartitions = 1,
            ReplicationFactor = 1
        };
}




