using backend.main.application.security.authorization;
using backend.main.shared.attributes.validation;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.moderation;

/// <summary>Whether an allowed action on club content is moderation, and in what capacity.</summary>
/// <param name="IsModeration">The caller is not the content's author.</param>
/// <param name="ActorClubRole">The caller's standing, recorded in the moderation log.</param>
public sealed record ModerationDecision(bool IsModeration, string ActorClubRole);

/// <summary>Shared rules for acting on content in a club (#402).</summary>
public static class ClubModeration
{
    public const int ReasonMaxLength = 500;
    public const int SnapshotMaxLength = 200;

    public const string ReasonRequiredMessage = "A reason is required to remove someone else's content.";
    public const string ReasonTooLongMessage = "The moderation reason cannot exceed 500 characters.";
    public const string ReasonInvalidMessage = "The moderation reason contains markup or unsupported characters.";
    public const string ReasonProhibitedMessage = "The moderation reason contains prohibited language.";

    /// <summary>
    /// Checks <paramref name="permission"/> on the content and throws the API's usual refusal,
    /// otherwise reports whether the caller is acting on someone else's content.
    /// </summary>
    public static async Task<ModerationDecision> AuthorizeContentAsync(
        this IResourceAuthorizer authorizer,
        ResourceActor actor,
        Permission permission,
        ClubContentResource content,
        PermissionMessages? messages = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await authorizer.EvaluateAsync(actor, permission, content, cancellationToken);
        if (!decision.IsAllowed)
            throw PermissionDenialErrors.For(permission, content, actor, decision, messages);

        return new ModerationDecision(actor.UserId != content.AuthorUserId, RoleOf(actor, decision.Held));
    }

    /// <summary>The trimmed reason for removing someone else's content; one is required.</summary>
    public static string RequireReason(string? reason) =>
        OptionalReason(reason) ?? throw new BadRequestException(ReasonRequiredMessage);

    /// <summary>
    /// The normalized reason, or <c>null</c> when none was given. Removals take the reason from
    /// the query string, where the JSON body's text policy does not run, so the same plain-text
    /// rules are applied here.
    /// </summary>
    public static string? OptionalReason(string? reason)
    {
        if (reason is null)
            return null;
        var normalized = ContentTextPolicy.Normalize(reason);
        if (normalized.Length == 0)
            return null;
        if (normalized.Length > ReasonMaxLength)
            throw new BadRequestException(ReasonTooLongMessage);
        return ContentTextPolicy.Validate(normalized, allowLineBreaks: true) switch
        {
            ContentTextViolation.None => normalized,
            ContentTextViolation.ProhibitedLanguage => throw new BadRequestException(ReasonProhibitedMessage),
            _ => throw new BadRequestException(ReasonInvalidMessage),
        };
    }

    /// <summary>Builds the log entry for a moderation action.</summary>
    public static ClubModerationAction Entry(
        ClubContentResource content,
        int actorUserId,
        ModerationDecision decision,
        string action,
        string? snapshot,
        string? reason) => new()
        {
            ClubId = content.ClubId,
            ActorUserId = actorUserId,
            ActorClubRole = decision.ActorClubRole,
            Action = action,
            ContentType = content.ContentType,
            ContentId = content.ContentId,
            AuthorUserId = content.AuthorUserId,
            ContentSnapshot = Snapshot(snapshot),
            Reason = reason,
            CreatedAt = DateTime.UtcNow,
        };

    private static string? Snapshot(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= SnapshotMaxLength ? trimmed : trimmed[..SnapshotMaxLength] + "…";
    }

    private static string RoleOf(ResourceActor actor, ResourceRelationship held)
    {
        if (held.HasFlag(ResourceRelationship.Owner))
            return ClubModerationRoles.Owner;
        if (held.HasFlag(ResourceRelationship.Manager))
            return ClubModerationRoles.Manager;
        if (held.HasFlag(ResourceRelationship.Volunteer))
            return ClubModerationRoles.Volunteer;
        return actor.IsAdmin ? ClubModerationRoles.Admin : string.Empty;
    }
}
