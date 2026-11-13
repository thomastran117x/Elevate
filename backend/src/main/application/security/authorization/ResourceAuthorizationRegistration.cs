using backend.main.features.clubs.access;
using backend.main.features.events.access;
using backend.main.features.media;
using backend.main.features.payment;

using Microsoft.AspNetCore.Authorization;

namespace backend.main.application.security.authorization;

public static class ResourceAuthorizationRegistration
{
    /// <summary>
    /// Registers the resource authorizer, its ASP.NET Core handler, the hub filter, and one
    /// evaluator per <see cref="ResourceKind"/>. Evaluators are scoped: they read the request's
    /// <c>AppDatabaseContext</c>, one query at a time.
    /// </summary>
    public static IServiceCollection AddResourceAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IResourceAuthorizer, ResourceAuthorizer>();
        services.AddScoped<IAuthorizationHandler, ResourcePermissionHandler>();
        services.AddScoped<ClubRelationshipResolver>();
        services.AddSingleton<ResourcePermissionHubFilter>();

        services.AddKeyedScoped<IResourcePermissionEvaluator, ClubPermissionEvaluator>(ResourceKind.Club);
        services.AddKeyedScoped<IResourcePermissionEvaluator, EventPermissionEvaluator>(ResourceKind.Event);
        services.AddKeyedScoped<IResourcePermissionEvaluator, EventSeriesPermissionEvaluator>(ResourceKind.EventSeries);
        services.AddKeyedScoped<IResourcePermissionEvaluator, PaymentPermissionEvaluator>(ResourceKind.Payment);
        services.AddKeyedScoped<IResourcePermissionEvaluator, MediaAssetPermissionEvaluator>(ResourceKind.MediaAsset);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, UserPermissionEvaluator>(ResourceKind.User);
        services.AddKeyedSingleton<IResourcePermissionEvaluator, AuthoredContentPermissionEvaluator>(ResourceKind.AuthoredContent);

        return services;
    }
}
