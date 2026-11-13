using backend.main.application.security.authorization;
using backend.main.features.clubs.access;
using backend.main.features.events.access;
using backend.main.features.media;
using backend.main.features.payment;
using backend.main.infrastructure.database.core;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Support;

/// <summary>Resource authorizers for service tests.</summary>
internal static class TestResourceAuthorizers
{
    /// <summary>
    /// The real evaluators that read only the database (clubs, series, payments, media, users,
    /// authored content), for harnesses that seed that state and want the production rules
    /// applied to it. Event permissions also need the visibility checker and are not included.
    /// </summary>
    public static IResourceAuthorizer Over(AppDatabaseContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ClubRelationshipResolver>();
        services.AddKeyedSingleton<IResourcePermissionEvaluator, ClubPermissionEvaluator>(ResourceKind.Club);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, EventSeriesPermissionEvaluator>(ResourceKind.EventSeries);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, PaymentPermissionEvaluator>(ResourceKind.Payment);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, MediaAssetPermissionEvaluator>(ResourceKind.MediaAsset);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, UserPermissionEvaluator>(ResourceKind.User);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, AuthoredContentPermissionEvaluator>(ResourceKind.AuthoredContent);
        return new ResourceAuthorizer(services.BuildServiceProvider());
    }

    /// <summary>
    /// A mock that denies everything until a test allows something with
    /// <see cref="Allow(Mock{IResourceAuthorizer}, Permission, IAuthorizationResource, int)"/>.
    /// Content-author checks keep their real rule, since the resource carries the author.
    /// </summary>
    public static Mock<IResourceAuthorizer> Denying()
    {
        var mock = new Mock<IResourceAuthorizer>();
        mock.Setup(authorizer => authorizer.EvaluateAsync(
                It.IsAny<ResourceActor>(),
                It.IsAny<Permission>(),
                It.IsAny<IAuthorizationResource>(),
                It.IsAny<CancellationToken>()))
            .Returns((ResourceActor actor, Permission permission, IAuthorizationResource resource, CancellationToken token) =>
                resource is AuthoredContentResource
                    ? new AuthoredContentPermissionEvaluator().EvaluateAsync(actor, permission, resource, token)
                    : Task.FromResult(new PermissionDecision(PermissionOutcome.Denied, ResourceRelationship.None)));
        return mock;
    }

    /// <summary>Allows (or, with <paramref name="outcome"/>, otherwise answers) one user on one resource.</summary>
    public static Mock<IResourceAuthorizer> Allow(
        this Mock<IResourceAuthorizer> mock,
        Permission permission,
        IAuthorizationResource resource,
        int userId,
        PermissionOutcome outcome = PermissionOutcome.Allowed)
    {
        mock.Setup(authorizer => authorizer.EvaluateAsync(
                It.Is<ResourceActor>(actor => actor.UserId == userId),
                permission,
                resource,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionDecision(outcome, ResourceRelationship.None));
        return mock;
    }
}
