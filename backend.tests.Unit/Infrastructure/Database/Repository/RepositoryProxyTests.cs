using System.Reflection;

using backend.main.infrastructure.database.repository;
using backend.main.shared.attributes.repository;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Infrastructure.Database.Repository;

public class RepositoryProxyTests
{
    [Fact]
    public async Task Proxy_ShouldExecuteAsyncMethodsThroughPolicy_ByDefault()
    {
        var policy = new RecordingPolicy();
        IPolicyBackedRepository proxy = RepositoryProxy<IPolicyBackedRepository>.Create(
            new PolicyBackedRepository(),
            policy);

        var result = await proxy.GetValueAsync();

        result.Should().Be("value");
        policy.GenericOperationNames.Should().ContainSingle()
            .Which.Should().Be("IPolicyBackedRepository.GetValueAsync");
        policy.GenericExecutions.Should().Be(1);
    }

    [Fact]
    public async Task Proxy_ShouldBypassPolicy_ForNoRetryMethods()
    {
        var policy = new RecordingPolicy();
        var target = new PolicyBackedRepository();
        IPolicyBackedRepository proxy = RepositoryProxy<IPolicyBackedRepository>.Create(
            target,
            policy);

        await proxy.SaveAsync();

        target.SaveCalls.Should().Be(1);
        policy.VoidExecutions.Should().Be(0);
    }

    [Fact]
    public async Task Proxy_ShouldHandleMissingEntity_ForVoidTaskMethods()
    {
        var policy = new RecordingPolicy();
        IMissingEntityRepository proxy = RepositoryProxy<IMissingEntityRepository>.Create(
            new MissingEntityRepository(),
            policy);

        var action = async () => await proxy.DeleteAsync();

        await action.Should().NotThrowAsync();
        policy.VoidExecutions.Should().Be(1);
    }

    [Fact]
    public async Task Proxy_ShouldReturnDefault_ForHandleMissingEntityTaskOfTMethods()
    {
        var policy = new RecordingPolicy();
        IMissingEntityRepository proxy = RepositoryProxy<IMissingEntityRepository>.Create(
            new MissingEntityRepository(),
            policy);

        var result = await proxy.TryDeleteAsync();

        result.Should().BeFalse();
        policy.GenericExecutions.Should().Be(1);
    }

    [Fact]
    public async Task Proxy_ShouldHandleMissingEntity_ForNoRetryTaskMethods()
    {
        var policy = new RecordingPolicy();
        INoRetryMissingRepository proxy = RepositoryProxy<INoRetryMissingRepository>.Create(
            new NoRetryMissingRepository(),
            policy);

        var result = await proxy.TryDeleteWithoutRetryAsync();

        result.Should().BeFalse();
        policy.GenericExecutions.Should().Be(0);
        policy.VoidExecutions.Should().Be(0);
    }

    [Fact]
    public void Proxy_ShouldPassThroughSynchronousMethods_WithoutPolicy()
    {
        var policy = new RecordingPolicy();
        IPolicyBackedRepository proxy = RepositoryProxy<IPolicyBackedRepository>.Create(
            new PolicyBackedRepository(),
            policy);

        var result = proxy.GetCount();

        result.Should().Be(7);
        policy.GenericExecutions.Should().Be(0);
        policy.VoidExecutions.Should().Be(0);
    }

    [Fact]
    public void GetOrBuildMethodMap_ShouldCacheResults_AndValidateCompatibility()
    {
        var first = RepositoryProxyReflection.GetOrBuildMethodMap(
            typeof(IPolicyBackedRepository),
            typeof(PolicyBackedRepository));
        var second = RepositoryProxyReflection.GetOrBuildMethodMap(
            typeof(IPolicyBackedRepository),
            typeof(PolicyBackedRepository));

        first.Should().BeSameAs(second);
        first.Keys.Should().Contain(method => method.Name == nameof(IPolicyBackedRepository.GetValueAsync));

        var action = () => RepositoryProxyReflection.GetOrBuildMethodMap(
            typeof(IPolicyBackedRepository),
            typeof(WrongRepository));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement interface*");
    }

    [Fact]
    public void GetOrResolveExecuteMethod_ShouldReturnCachedClosedGenericMethod_AndRejectMissingBaseType()
    {
        var first = RepositoryProxyReflection.GetOrResolveExecuteMethod(
            typeof(RepositoryProxy<IPolicyBackedRepository>),
            typeof(string),
            handleMissingEntity: false);
        var second = RepositoryProxyReflection.GetOrResolveExecuteMethod(
            typeof(RepositoryProxy<IPolicyBackedRepository>),
            typeof(string),
            handleMissingEntity: false);
        var missing = RepositoryProxyReflection.GetOrResolveExecuteMethod(
            typeof(RepositoryProxy<IPolicyBackedRepository>),
            typeof(bool),
            handleMissingEntity: true);

        first.Should().BeSameAs(second);
        first.Name.Should().Be("ExecuteWithPolicy");
        missing.Name.Should().Be("ExecuteWithPolicyAndHandleMissingEntity");

        var action = () => RepositoryProxyReflection.GetOrResolveExecuteMethod(
            typeof(object),
            typeof(string),
            handleMissingEntity: false);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*has no base type*");
    }

    public interface IPolicyBackedRepository
    {
        Task<string> GetValueAsync();

        [NoRetry]
        Task SaveAsync();

        int GetCount();
    }

    public interface IMissingEntityRepository
    {
        [HandleMissingEntity]
        Task DeleteAsync();

        [HandleMissingEntity]
        Task<bool> TryDeleteAsync();
    }

    public interface INoRetryMissingRepository
    {
        [NoRetry]
        [HandleMissingEntity]
        Task<bool> TryDeleteWithoutRetryAsync();
    }

    private sealed class PolicyBackedRepository : IPolicyBackedRepository
    {
        public int SaveCalls { get; private set; }

        public Task<string> GetValueAsync() => Task.FromResult("value");

        public Task SaveAsync()
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public int GetCount() => 7;
    }

    private sealed class MissingEntityRepository : IMissingEntityRepository
    {
        public Task DeleteAsync() =>
            Task.FromException(new DbUpdateConcurrencyException("missing"));

        public Task<bool> TryDeleteAsync() =>
            Task.FromException<bool>(new DbUpdateException("missing", new Exception("inner")));
    }

    private sealed class NoRetryMissingRepository : INoRetryMissingRepository
    {
        public Task<bool> TryDeleteWithoutRetryAsync() =>
            Task.FromException<bool>(new DbUpdateConcurrencyException("missing"));
    }

    private sealed class WrongRepository;

    private sealed class RecordingPolicy : IRepositoryResiliencePolicy
    {
        public int GenericExecutions { get; private set; }
        public int VoidExecutions { get; private set; }
        public List<string> GenericOperationNames { get; } = [];
        public List<string> VoidOperationNames { get; } = [];
        public bool IsDatabaseHealthy => true;

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            string operationName,
            CancellationToken ct = default)
        {
            GenericExecutions++;
            GenericOperationNames.Add(operationName);
            return await action(ct);
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            string operationName,
            CancellationToken ct = default)
        {
            VoidExecutions++;
            VoidOperationNames.Add(operationName);
            await action(ct);
        }
    }
}
