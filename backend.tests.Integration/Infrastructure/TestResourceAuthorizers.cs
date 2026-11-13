using backend.main.application.security.authorization;
using backend.main.features.clubs.access;
using backend.main.infrastructure.database.core;

using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Infrastructure;

/// <summary>Resource authorizers for services the tests construct by hand.</summary>
internal static class TestResourceAuthorizers
{
    /// <summary>The production club, user and authored-content rules over the test database.</summary>
    public static IResourceAuthorizer Over(AppDatabaseContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ClubRelationshipResolver>();
        services.AddKeyedSingleton<IResourcePermissionEvaluator, ClubPermissionEvaluator>(ResourceKind.Club);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, UserPermissionEvaluator>(ResourceKind.User);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, AuthoredContentPermissionEvaluator>(ResourceKind.AuthoredContent);
        return new ResourceAuthorizer(services.BuildServiceProvider());
    }
}
