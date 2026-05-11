using System.Reflection;

using backend.main.attributes.repository;

using Microsoft.EntityFrameworkCore;

namespace backend.main.infrastructure.database.repository
{
    /// Intercepts repository interface calls. Applies retry/backoff and optional missing-entity
    /// handling from attributes. Attribute precedence (when using IRepositoryAttributeResolver):
    /// interface method → implementation method → implementation class → interface type.
    /// Reflection and caches are delegated to <see cref="RepositoryProxyReflection"/>.
    public class RepositoryProxy<T> : DispatchProxy
        where T : class
    {
        private T _target = null!;
        private IRepositoryResiliencePolicy _policy = null!;
        private IRepositoryAttributeResolver? _resolver;
        private Dictionary<MethodInfo, MethodInfo> _interfaceToImpl = null!;

        /// Creates a proxy that wraps target. Validates interface mapping at creation so any
        /// reflection failure happens during DI resolution, not on first request.
        /// Throws InvalidOperationException if target does not implement T or mapping fails.
        public static T Create(
            T target,
            IRepositoryResiliencePolicy policy,
            IRepositoryAttributeResolver? resolver = null
        )
        {
            object proxy = Create<T, RepositoryProxy<T>>();
            var typed = (RepositoryProxy<T>)proxy;
            typed._target = target;
            typed._policy = policy;
            typed._resolver = resolver;
            typed._interfaceToImpl = RepositoryProxyReflection.GetOrBuildMethodMap(
                typeof(T),
                target.GetType()
            );
            return (T)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || args is null)
                return null;

            MethodInfo implMethod = ResolveImplementationMethod(targetMethod);
            RepositoryMethodBehavior behavior = GetBehavior(targetMethod, implMethod);

            if (behavior.NoRetry)
                return InvokeNoRetry(
                    implMethod,
                    args,
                    targetMethod.ReturnType,
                    behavior.HandleMissingEntity
                );

            return InvokeWithResilience(
                implMethod,
                args,
                targetMethod,
                behavior.HandleMissingEntity
            );
        }

        private MethodInfo ResolveImplementationMethod(MethodInfo targetMethod)
        {
            if (_interfaceToImpl.TryGetValue(targetMethod, out var impl))
                return impl;
            throw new InvalidOperationException(
                $"Interface method {targetMethod.DeclaringType?.Name}.{targetMethod.Name} is not implemented by {_target.GetType().Name}. "
                    + "Ensure the interface method is implemented (not explicitly only)."
            );
        }

        private object? InvokeNoRetry(
            MethodInfo implMethod,
            object?[] args,
            Type returnType,
            bool handleMissingEntity
        )
        {
            object? result = InvokeTarget(implMethod, args);
            if (handleMissingEntity && result is Task task)
                return WrapTaskWithMissingEntityHandling(task, returnType);
            return result;
        }

        private object? InvokeWithResilience(
            MethodInfo implMethod,
            object?[] args,
            MethodInfo targetMethod,
            bool handleMissingEntity
        )
        {
            string operationName = BuildOperationName(targetMethod);
            Type returnType = targetMethod.ReturnType;

            if (returnType == typeof(Task))
                return ExecuteVoidThroughPolicy(
                    implMethod,
                    args,
                    operationName,
                    handleMissingEntity
                );

            if (IsTaskOfT(returnType))
            {
                Type resultType = returnType.GetGenericArguments()[0];
                return ExecuteTaskOfTThroughPolicy(
                    implMethod,
                    args,
                    operationName,
                    resultType,
                    handleMissingEntity
                );
            }

            return InvokeTarget(implMethod, args);
        }

        private RepositoryMethodBehavior GetBehavior(
            MethodInfo interfaceMethod,
            MethodInfo implMethod
        )
        {
            if (_resolver != null)
                return _resolver.GetBehavior(interfaceMethod, implMethod, _target.GetType());
            bool noRetry = interfaceMethod.GetCustomAttribute<NoRetryAttribute>() != null;
            bool handleMissing =
                interfaceMethod.GetCustomAttribute<HandleMissingEntityAttribute>() != null;
            return new RepositoryMethodBehavior(noRetry, handleMissing);
        }

        private static string BuildOperationName(MethodInfo targetMethod) =>
            $"{targetMethod.DeclaringType?.Name}.{targetMethod.Name}";

        private static bool IsTaskOfT(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);

        private object? InvokeTarget(MethodInfo implMethod, object?[] args)
        {
            try
            {
                return implMethod.Invoke(_target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        private object ExecuteVoidThroughPolicy(
            MethodInfo implMethod,
            object?[] args,
            string operationName,
            bool handleMissingEntity
        )
        {
            return _policy.ExecuteAsync(
                _ =>
                    handleMissingEntity
                        ? InvokeVoidAndHandleMissingEntityAsync(implMethod, args)
                        : (Task)InvokeTarget(implMethod, args)!,
                operationName,
                default
            );
        }

        private object? ExecuteTaskOfTThroughPolicy(
            MethodInfo implMethod,
            object?[] args,
            string operationName,
            Type resultType,
            bool handleMissingEntity
        )
        {
            MethodInfo methodInfo = RepositoryProxyReflection.GetOrResolveExecuteMethod(
                GetType(),
                resultType,
                handleMissingEntity
            );
            try
            {
                return methodInfo.Invoke(this, new object?[] { implMethod, args, operationName });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        private async Task<TResult> ExecuteWithPolicy<TResult>(
            MethodInfo implMethod,
            object?[] args,
            string operationName
        )
        {
            return await _policy.ExecuteAsync(
                _ => (Task<TResult>)InvokeTarget(implMethod, args)!,
                operationName,
                default
            );
        }

        private async Task<TResult> ExecuteWithPolicyAndHandleMissingEntity<TResult>(
            MethodInfo implMethod,
            object?[] args,
            string operationName
        )
        {
            return await _policy.ExecuteAsync(
                _ => InvokeAndHandleMissingEntityAsync<TResult>(implMethod, args),
                operationName,
                default
            );
        }

        private async Task InvokeVoidAndHandleMissingEntityAsync(
            MethodInfo implMethod,
            object?[] args
        )
        {
            try
            {
                var task = (Task)InvokeTarget(implMethod, args)!;
                await task;
            }
            catch (Exception ex) when (IsMissingEntityOrConcurrency(ex))
            {
                // Graceful for saga: treat as no-op success.
            }
        }

        private async Task<TResult> InvokeAndHandleMissingEntityAsync<TResult>(
            MethodInfo implMethod,
            object?[] args
        )
        {
            try
            {
                var task = (Task<TResult>)InvokeTarget(implMethod, args)!;
                return await task;
            }
            catch (Exception ex) when (IsMissingEntityOrConcurrency(ex))
            {
                return default!;
            }
        }

        /// DbUpdateConcurrencyException (missing row) or DbUpdateException (e.g. FK when referenced entity deleted).
        private static bool IsMissingEntityOrConcurrency(Exception ex) =>
            ex is DbUpdateConcurrencyException or DbUpdateException;

        private static object WrapTaskWithMissingEntityHandling(Task task, Type returnType)
        {
            if (returnType == typeof(Task))
                return WrapVoidAsync(task);
            Type resultType = returnType.GetGenericArguments()[0];
            MethodInfo? wrap = typeof(RepositoryProxy<T>).GetMethod(
                nameof(WrapTypedAsync),
                BindingFlags.NonPublic | BindingFlags.Static
            );
            if (wrap is null)
                throw new InvalidOperationException(
                    $"Could not find method {nameof(WrapTypedAsync)} on {nameof(RepositoryProxy<T>)}."
                );
            return wrap.MakeGenericMethod(resultType).Invoke(null, new object[] { task })!;
        }

        private static async Task WrapVoidAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex) when (IsMissingEntityOrConcurrency(ex)) { }
        }

        private static async Task<TResult> WrapTypedAsync<TResult>(Task task)
        {
            try
            {
                return await ((Task<TResult>)task);
            }
            catch (Exception ex) when (IsMissingEntityOrConcurrency(ex))
            {
                return default!;
            }
        }
    }
}
