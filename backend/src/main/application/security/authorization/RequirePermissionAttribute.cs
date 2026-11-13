namespace backend.main.application.security.authorization;

/// <summary>
/// Declares that a controller action or hub method requires a catalog permission on the resource
/// identified by one of its parameters. <see cref="ResourcePermissionActionFilter"/> and
/// <see cref="ResourcePermissionHubFilter"/> enforce it; the Auth Contract Gate checks every
/// declaration against the approved matrix.
/// </summary>
/// <remarks>
/// The edge check refuses callers who are denied access to a resource that exists. A missing
/// resource is left to the action, so its usual not-found response, and the order of its other
/// checks, are unchanged. Services keep their own check of the same permission.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    public RequirePermissionAttribute(string permission, string resourceParameter)
    {
        PermissionName = permission;
        ResourceParameter = resourceParameter;
    }

    public string PermissionName
    {
        get;
    }

    /// <summary>The action or hub-method parameter (or route value) holding the resource id.</summary>
    public string ResourceParameter
    {
        get;
    }

    /// <summary>Overrides the permission's 403 message, to keep an endpoint's existing wording.</summary>
    public string? DenialMessage
    {
        get; init;
    }

    /// <summary>Overrides the permission's 401 message for anonymous callers.</summary>
    public string? AnonymousDenialMessage
    {
        get; init;
    }

    public Permission Permission => Permissions.Get(PermissionName);

    internal PermissionMessages Messages => new(DenialMessage, AnonymousDenialMessage);

    /// <summary>Builds the resource reference from a bound parameter or route value.</summary>
    public IAuthorizationResource? CreateResource(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };

        if (string.IsNullOrWhiteSpace(text))
            return null;

        var kind = Permission.Resource;
        if (kind == ResourceKind.MediaAsset)
            return Guid.TryParse(text, out var publicId) ? new MediaAssetResource(publicId) : null;

        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
            return null;

        return kind switch
        {
            ResourceKind.Club => new ClubResource(id),
            ResourceKind.Event => new EventResource(id),
            ResourceKind.EventSeries => new EventSeriesResource(id),
            ResourceKind.Payment => new PaymentResource(id),
            ResourceKind.User => new UserResource(id),
            _ => throw new InvalidOperationException(
                $"Permission '{PermissionName}' cannot be declared on an endpoint; it is checked in the service."),
        };
    }
}
