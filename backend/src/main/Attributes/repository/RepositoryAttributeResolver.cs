using System.Reflection;
using Microsoft.Extensions.Logging;

namespace backend.main.attributes.repository
{
    /// Resolves NoRetry and HandleMissingEntity with precedence: interface method → impl method → impl class → interface.
    /// First occurrence of each attribute wins. When conflicting attributes appear at different levels (e.g. NoRetry
    /// on one level, RetryOnTransientFailure on another), logs a warning if a logger is provided.
    public sealed class RepositoryAttributeResolver : IRepositoryAttributeResolver
    {
        private const string LevelInterfaceMethod = "interface method";
        private const string LevelImplMethod = "implementation method";
        private const string LevelImplClass = "implementation class";
        private const string LevelInterface = "interface type";

        private readonly ILogger<RepositoryAttributeResolver>? _logger;

        public RepositoryAttributeResolver(ILogger<RepositoryAttributeResolver>? logger = null)
        {
            _logger = logger;
        }

        public RepositoryMethodBehavior GetBehavior(
            MethodInfo interfaceMethod,
            MethodInfo implementationMethod,
            Type implementationType
        )
        {
            Type interfaceType = interfaceMethod.DeclaringType!;

            string? noRetryAt = FirstLevelWithAttribute<NoRetryAttribute>(
                interfaceMethod,
                implementationMethod,
                implementationType,
                interfaceType
            );
            string? retryOnTransientAt = FirstLevelWithAttribute<RetryOnTransientFailureAttribute>(
                interfaceMethod,
                implementationMethod,
                implementationType,
                interfaceType
            );
            string? handleMissingAt = FirstLevelWithAttribute<HandleMissingEntityAttribute>(
                interfaceMethod,
                implementationMethod,
                implementationType,
                interfaceType
            );

            bool noRetry = noRetryAt is not null;
            bool handleMissing = handleMissingAt is not null;

            if (
                _logger is not null
                && noRetryAt is not null
                && retryOnTransientAt is not null
                && noRetryAt != retryOnTransientAt
            )
            {
                _logger.LogWarning(
                    "Conflicting repository attributes for {Method}: NoRetry at {NoRetryLevel}, RetryOnTransientFailure at {RetryLevel}. Using NoRetry (first in precedence).",
                    $"{interfaceMethod.DeclaringType?.Name}.{interfaceMethod.Name}",
                    noRetryAt,
                    retryOnTransientAt
                );
            }

            return new RepositoryMethodBehavior(noRetry, handleMissing);
        }

        /// Returns the first level name where TAttr is found, or null if not found.
        private static string? FirstLevelWithAttribute<TAttr>(
            MethodInfo interfaceMethod,
            MethodInfo implementationMethod,
            Type implementationType,
            Type interfaceType
        )
            where TAttr : Attribute
        {
            if (interfaceMethod.GetCustomAttribute<TAttr>() != null)
                return LevelInterfaceMethod;
            if (implementationMethod.GetCustomAttribute<TAttr>() != null)
                return LevelImplMethod;
            if (implementationType.GetCustomAttribute<TAttr>() != null)
                return LevelImplClass;
            if (interfaceType.GetCustomAttribute<TAttr>() != null)
                return LevelInterface;
            return null;
        }
    }
}
