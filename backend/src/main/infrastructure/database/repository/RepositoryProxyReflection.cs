using System.Collections.Concurrent;
using System.Reflection;

namespace backend.main.infrastructure.database.repository
{
    /// Centralizes reflection and caching for repository proxy: interface→implementation method maps
    /// and generic execute-method resolution. Reduces proxy complexity and allows reuse/testing.
    public static class RepositoryProxyReflection
    {
        private static readonly ConcurrentDictionary<
            (Type InterfaceType, Type ImplType),
            Dictionary<MethodInfo, MethodInfo>
        > MethodMapCache = new();
        private static readonly ConcurrentDictionary<
            (Type ProxyBaseType, Type ResultType, bool HandleMissing),
            MethodInfo
        > ExecuteMethodCache = new();

        /// Returns cached method map for (interfaceType, implType) or builds and caches it.
        /// Throws InvalidOperationException if implType does not implement interfaceType or mapping fails.
        public static Dictionary<MethodInfo, MethodInfo> GetOrBuildMethodMap(
            Type interfaceType,
            Type implType
        )
        {
            var key = (interfaceType, implType);
            if (MethodMapCache.TryGetValue(key, out var cached))
                return cached;
            return MethodMapCache.GetOrAdd(key, _ => BuildMethodMap(interfaceType, implType));
        }

        /// Resolves the closed generic ExecuteWithPolicy or ExecuteWithPolicyAndHandleMissingEntity method.
        /// Cached per (proxyBaseType, resultType, handleMissingEntity).
        public static MethodInfo GetOrResolveExecuteMethod(
            Type proxyType,
            Type resultType,
            bool handleMissingEntity
        )
        {
            Type? proxyBaseType = proxyType.BaseType;
            if (proxyBaseType is null || proxyBaseType == typeof(object))
            {
                throw new InvalidOperationException(
                    $"Proxy type {proxyType.Name} has no base type for method resolution."
                );
            }

            var key = (proxyBaseType, resultType, handleMissingEntity);
            if (ExecuteMethodCache.TryGetValue(key, out var cached))
                return cached;

            const BindingFlags flags =
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            string methodName = handleMissingEntity
                ? "ExecuteWithPolicyAndHandleMissingEntity"
                : "ExecuteWithPolicy";
            for (
                Type? type = proxyType;
                type is not null && type != typeof(object);
                type = type.BaseType
            )
            {
                MethodInfo? method = type.GetMethod(methodName, flags);
                if (method is not null)
                {
                    MethodInfo closed = method.MakeGenericMethod(resultType);
                    return ExecuteMethodCache.GetOrAdd(key, closed);
                }
            }
            throw new InvalidOperationException(
                $"Could not find {methodName} method on proxy type hierarchy."
            );
        }

        private static Dictionary<MethodInfo, MethodInfo> BuildMethodMap(
            Type interfaceType,
            Type implType
        )
        {
            ValidateInterfaceCompatibility(interfaceType, implType);

            InterfaceMapping map;
            try
            {
                map = implType.GetInterfaceMap(interfaceType);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to get interface map for {interfaceType.Name} on {implType.Name}. "
                        + "Ensure the implementation type implements the interface.",
                    ex
                );
            }

            var dict = new Dictionary<MethodInfo, MethodInfo>(map.InterfaceMethods.Length);
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
                dict[map.InterfaceMethods[i]] = map.TargetMethods[i];
            return dict;
        }

        private static void ValidateInterfaceCompatibility(Type interfaceType, Type implType)
        {
            if (!interfaceType.IsAssignableFrom(implType))
            {
                throw new InvalidOperationException(
                    $"Target type {implType.Name} does not implement interface {interfaceType.Name}. "
                        + "Register the interface (e.g. IUserRepository) and use the proxy; do not inject the concrete type directly."
                );
            }
        }
    }
}
