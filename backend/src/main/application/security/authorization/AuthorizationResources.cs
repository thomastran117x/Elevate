namespace backend.main.application.security.authorization;

/// <summary>
/// A reference to the resource a permission is checked against. Only the key travels with it:
/// evaluators load the authoritative state from the database.
/// </summary>
public interface IAuthorizationResource
{
    ResourceKind Kind
    {
        get;
    }

    /// <summary>The message a missing resource is reported with.</summary>
    string NotFoundMessage
    {
        get;
    }
}

public sealed record ClubResource(int ClubId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.Club;
    public string NotFoundMessage => $"Club {ClubId} not found";
}

public sealed record EventResource(int EventId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.Event;
    public string NotFoundMessage => $"Event {EventId} not found";
}

public sealed record EventSeriesResource(int SeriesId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.EventSeries;
    public string NotFoundMessage => $"Series {SeriesId} not found";
}

public sealed record PaymentResource(int PaymentId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.Payment;
    public string NotFoundMessage => $"Payment {PaymentId} not found.";
}

public sealed record MediaAssetResource(Guid PublicId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.MediaAsset;
    public string NotFoundMessage => "Media asset not found.";
}

/// <summary>A user whose private data is being read.</summary>
public sealed record UserResource(int UserId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.User;
    public string NotFoundMessage => $"User {UserId} not found";
}

/// <summary>
/// Content the caller already loaded (a discussion, reply, comment or review). The author is part
/// of the reference because the service has just read it under its own existence checks.
/// </summary>
public sealed record AuthoredContentResource(string ContentType, int ContentId, int AuthorUserId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.AuthoredContent;
    public string NotFoundMessage => $"{ContentType} with ID {ContentId} was not found.";
}

/// <summary>
/// Content in a club that the service has already loaded and matched to the club (a post,
/// discussion, reply, comment or review). The evaluator reads the club, the caller's and the
/// author's staff roles from the database, so moderation follows the current ranks.
/// </summary>
public sealed record ClubContentResource(int ClubId, string ContentType, int ContentId, int AuthorUserId) : IAuthorizationResource
{
    public ResourceKind Kind => ResourceKind.ClubContent;
    public string NotFoundMessage => $"Club {ClubId} not found";
}
