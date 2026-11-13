using backend.main.application.security.authorization;

namespace backend.tests.Unit.Support;

/// <summary>
/// An in-memory <see cref="IResourceAuthorizer"/> for services tested with mocked repositories.
/// Clubs, staff and members are declared by the test, and decisions apply the production
/// permission rules (<see cref="PermissionDecision.Decide"/>) to them, so a test that says "a
/// private-club outsider is refused" still exercises the real rule rather than a canned answer.
/// </summary>
internal sealed class FakeClubAuthorizer : IResourceAuthorizer
{
    private readonly Dictionary<int, (bool IsPrivate, int? OwnerId)> _clubs = new();
    private readonly Dictionary<(int ClubId, int UserId), ResourceRelationship> _relationships = new();

    /// <summary>Every permission evaluated, in order.</summary>
    public List<(Permission Permission, IAuthorizationResource Resource)> Evaluated { get; } = [];

    public FakeClubAuthorizer Club(int clubId, bool isPrivate = false, int? ownerId = null)
    {
        _clubs[clubId] = (isPrivate, ownerId);
        return this;
    }

    public FakeClubAuthorizer Member(int clubId, int userId) => Grant(clubId, userId, ResourceRelationship.Member);

    public FakeClubAuthorizer Volunteer(int clubId, int userId) => Grant(clubId, userId, ResourceRelationship.Volunteer);

    public FakeClubAuthorizer Manager(int clubId, int userId) => Grant(clubId, userId, ResourceRelationship.Manager);

    /// <summary>Removes every staff role and membership the user holds in the club.</summary>
    public FakeClubAuthorizer Revoke(int clubId, int userId)
    {
        _relationships.Remove((clubId, userId));
        return this;
    }

    public Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        Evaluated.Add((permission, resource));

        switch (resource)
        {
            case ClubResource club:
                if (!_clubs.TryGetValue(club.ClubId, out var state))
                    return Task.FromResult(PermissionDecision.NotFound);

                var held = actor.BaseRelationships();
                if (actor.UserId is int userId)
                {
                    if (state.OwnerId == userId)
                        held |= ResourceRelationship.Owner;
                    held |= _relationships.GetValueOrDefault((club.ClubId, userId));
                }

                return Task.FromResult(PermissionDecision.Decide(permission, held, resourceIsPublic: !state.IsPrivate));

            case AuthoredContentResource:
                return new AuthoredContentPermissionEvaluator().EvaluateAsync(actor, permission, resource, cancellationToken);

            default:
                throw new NotSupportedException($"{nameof(FakeClubAuthorizer)} does not model {resource.Kind} resources.");
        }
    }

    private FakeClubAuthorizer Grant(int clubId, int userId, ResourceRelationship relationship)
    {
        _relationships[(clubId, userId)] = _relationships.GetValueOrDefault((clubId, userId)) | relationship;
        return this;
    }
}
