using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Infrastructure;

internal sealed class AuthApiTestAppPool : IAsyncDisposable
{
    private const int PoolSize = 4;
    private static readonly Lazy<Task<AuthApiTestAppPool>> Instance =
        new(CreateAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Channel<Slot> _available;
    private readonly Slot[] _slots;
    private int _disposed;

    private AuthApiTestAppPool(IEnumerable<Slot> slots)
    {
        _slots = slots.ToArray();
        _available = Channel.CreateBounded<Slot>(new BoundedChannelOptions(PoolSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        foreach (var slot in _slots)
            _available.Writer.TryWrite(slot);
    }

    internal static async ValueTask DisposeIfCreatedAsync()
    {
        if (!Instance.IsValueCreated)
            return;

        var pool = await Instance.Value;
        await pool.DisposeAsync();
    }

    public static async Task<Lease> AcquireAsync(
        Action<IServiceCollection>? serviceOverrides,
        IReadOnlyDictionary<string, string?>? configurationOverrides)
    {
        var pool = await Instance.Value;
        var slot = await pool._available.Reader.ReadAsync();
        TestWebApplicationFactory? transientFactory = null;

        try
        {
            await slot.ResetAsync();
            var usesOverrides = serviceOverrides is not null || configurationOverrides is not null;
            var factory = slot.Factory;
            if (usesOverrides)
            {
                transientFactory = new TestWebApplicationFactory(
                    slot.Environment,
                    slot.Database.ConnectionString,
                    slot.Resources,
                    serviceOverrides,
                    configurationOverrides);
                factory = transientFactory;
            }

            factory.ResetTestDoubles();
            return new Lease(pool, slot, factory, transientFactory);
        }
        catch
        {
            transientFactory?.Dispose();
            await pool._available.Writer.WriteAsync(slot);
            throw;
        }
    }

    private static async Task<AuthApiTestAppPool> CreateAsync()
    {
        var environment = await IntegrationTestFixture.GetEnvironmentAsync();
        var slots = await Task.WhenAll(
            Enumerable.Range(0, PoolSize).Select(slot => Slot.CreateAsync(environment, slot)));
        return new AuthApiTestAppPool(slots);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _available.Writer.TryComplete();
        foreach (var slot in _slots)
            await slot.DisposeAsync();
    }

    internal sealed class Lease : IAsyncDisposable
    {
        private readonly AuthApiTestAppPool _pool;
        private readonly Slot _slot;
        private readonly TestWebApplicationFactory? _transientFactory;
        private int _disposed;

        public TestWebApplicationFactory Factory { get; }

        public PostgresTestDatabase Database => _slot.Database;

        public IntegrationTestEnvironment Environment => _slot.Environment;

        public TestResourceNamespace Resources => _slot.Resources;

        internal Lease(
            AuthApiTestAppPool pool,
            Slot slot,
            TestWebApplicationFactory factory,
            TestWebApplicationFactory? transientFactory)
        {
            _pool = pool;
            _slot = slot;
            Factory = factory;
            _transientFactory = transientFactory;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _transientFactory?.Dispose();
            await _pool._available.Writer.WriteAsync(_slot);
        }
    }

    internal sealed class Slot : IAsyncDisposable
    {
        public IntegrationTestEnvironment Environment { get; }
        public PostgresTestDatabase Database { get; }
        public TestResourceNamespace Resources { get; }
        public TestWebApplicationFactory Factory { get; }

        private Slot(
            IntegrationTestEnvironment environment,
            PostgresTestDatabase database,
            TestResourceNamespace resources,
            TestWebApplicationFactory factory)
        {
            Environment = environment;
            Database = database;
            Resources = resources;
            Factory = factory;
        }

        public static async Task<Slot> CreateAsync(
            IntegrationTestEnvironment environment,
            int slotNumber)
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var resources = new TestResourceNamespace(slotNumber);
            await environment.EnsureKafkaTopicsExistAsync(resources.KafkaTopics);

            var factory = new TestWebApplicationFactory(
                environment,
                database.ConnectionString,
                resources);

            // Force host construction once. Each lease creates a fresh client.
            using var warmupClient = factory.CreateClient();
            return new Slot(environment, database, resources, factory);
        }

        public async Task ResetAsync()
        {
            await Task.WhenAll(
                Database.ResetAsync(),
                Environment.ResetSharedStateAsync(
                    Resources.RedisDatabase,
                    Resources.ElasticsearchIndices));
            Factory.ResetTestDoubles();
        }

        public async ValueTask DisposeAsync()
        {
            Factory.Dispose();
            await Database.DisposeAsync();
        }
    }
}
