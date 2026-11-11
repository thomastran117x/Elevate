using System.Reflection;

using backend.main.features.cache;

using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Infrastructure;

/// <summary>
/// Makes chosen Redis scripts report the cache as unreachable while everything else keeps working,
/// to test what survives a Redis failure part way through an operation.
/// </summary>
public sealed class CacheScriptOutage
{
    /// <summary>The scripts that currently fail. Null means none do.</summary>
    public Func<string, bool>? Failing
    {
        get; set;
    }

    /// <summary>Wraps the host's registered <see cref="ICacheService"/>.</summary>
    public void Install(IServiceCollection services)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(ICacheService));
        services.Remove(descriptor);
        services.AddSingleton<ICacheService>(provider =>
        {
            var inner = descriptor.ImplementationInstance as ICacheService
                ?? (descriptor.ImplementationFactory is { } factory
                    ? (ICacheService)factory(provider)
                    : (ICacheService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
            return OutageProxy.Wrap(inner, this);
        });
    }

    public class OutageProxy : DispatchProxy
    {
        private ICacheService _inner = null!;
        private CacheScriptOutage _outage = null!;

        internal static ICacheService Wrap(ICacheService inner, CacheScriptOutage outage)
        {
            var proxy = Create<ICacheService, OutageProxy>();
            var typed = (OutageProxy)(object)proxy;
            typed._inner = inner;
            typed._outage = outage;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICacheService.TryEvalAsync)
                && args is [string script, ..]
                && _outage.Failing?.Invoke(script) == true)
            {
                return Task.FromResult(CacheScriptResult.Unavailable);
            }

            try
            {
                return targetMethod!.Invoke(_inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }
    }
}
