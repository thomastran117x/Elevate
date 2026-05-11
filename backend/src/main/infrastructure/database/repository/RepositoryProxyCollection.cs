using backend.main.attributes.repository;

namespace backend.main.infrastructure.database.repository
{
    /// Extension methods to register repository interfaces with a resilience proxy. The concrete
    /// implementation is not registered; only the interface is resolvable, avoiding accidental
    /// injection of the concrete type.
    public static class RepositoryProxyServiceCollectionExtensions
    {
        /// Registers TInterface so that resolution returns a proxy wrapping an instance of TImpl
        /// (created with constructor injection from the container). Uses the shared resilience
        /// policy and optional attribute resolver for retry/missing-entity behavior.
        public static IServiceCollection AddRepositoryWithProxy<TInterface, TImpl>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
            where TInterface : class
            where TImpl : class, TInterface
        {
            services.Add(
                new ServiceDescriptor(
                    typeof(TInterface),
                    sp =>
                    {
                        var target = (TImpl)ActivatorUtilities.CreateInstance(sp, typeof(TImpl));
                        var policy = sp.GetRequiredService<IRepositoryResiliencePolicy>();
                        var resolver = sp.GetService<IRepositoryAttributeResolver>();
                        return RepositoryProxy<TInterface>.Create(target, policy, resolver);
                    },
                    lifetime
                )
            );
            return services;
        }
    }
}
