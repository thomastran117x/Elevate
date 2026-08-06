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

[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]

namespace backend.tests.Integration.Infrastructure;

public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IntegrationTestEnvironment? _environment;

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
            return _environment;
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task InitializeAsync() => GetEnvironmentAsync();

    public Task DisposeAsync() => Task.CompletedTask;
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
    private const string ElasticsearchEventsIndex = "events";
    private const string ElasticsearchClubsIndex = "clubs";
    private const string ElasticsearchClubPostsIndex = "club_posts";

    private readonly RedisContainer _redisContainer;
    private readonly KafkaContainer _kafkaContainer;
    private readonly PostgreSqlContainer _postgresContainer;
    private readonly IContainer _elasticsearchContainer;

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
            .WithDatabase(DefaultDatabase)
            .WithUsername(PostgresUser)
            .WithPassword(PostgresPassword)
            .Build();

        var redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        var kafkaContainer = new KafkaBuilder().Build();

        var elasticsearchContainer = new ContainerBuilder()
            .WithImage(ElasticsearchImage)
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

        await environment.StartAsync();
        return environment;
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

    public KafkaTopicProbe CreateKafkaProbe() =>
        new(KafkaBootstrapServers);

    public async Task ResetSharedStateAsync()
    {
        await FlushRedisAsync();
        await DeleteElasticsearchIndexAsync(ElasticsearchEventsIndex);
        await DeleteElasticsearchIndexAsync(ElasticsearchClubsIndex);
        await DeleteElasticsearchIndexAsync(ElasticsearchClubPostsIndex);
        await EnsureKafkaTopicsExistAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _elasticsearchContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    private async Task StartAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();
        await _elasticsearchContainer.StartAsync();

        PostgresServerConnectionString = BuildPostgresConnectionString(DefaultDatabase);
        RedisConnectionString = BuildRedisConnectionString();
        KafkaBootstrapServers = _kafkaContainer.GetBootstrapAddress();
        ElasticsearchUrl =
            $"http://{_elasticsearchContainer.Hostname}:{_elasticsearchContainer.GetMappedPublicPort(9200)}";

        SetEnvironmentVariables();
        await EnsureKafkaTopicsExistAsync();
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

    private async Task FlushRedisAsync()
    {
        using var mux = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
        var server = mux.GetServer(mux.GetEndPoints().First());
        await server.FlushDatabaseAsync();
    }

    private async Task DeleteElasticsearchIndexAsync(string indexName)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(ElasticsearchUrl)
        };

        using var response = await httpClient.DeleteAsync($"/{indexName}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        response.EnsureSuccessStatusCode();
    }

    private async Task EnsureKafkaTopicsExistAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = KafkaBootstrapServers
        }).Build();

        try
        {
            await admin.CreateTopicsAsync(
                [
                    CreateTopicSpecification(EmailTopicName),
                    CreateTopicSpecification(SmsTopicName),
                    CreateTopicSpecification(EmailStatusTopicName)
                ]);
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




