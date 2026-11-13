using backend.main.application.security.authorization;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.payment;

/// <summary>Decides <c>payment.owner</c>: a payment belongs to the user who made it.</summary>
public sealed class PaymentPermissionEvaluator : IResourcePermissionEvaluator
{
    private readonly AppDatabaseContext _db;

    public PaymentPermissionEvaluator(AppDatabaseContext db)
    {
        _db = db;
    }

    public async Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        var paymentId = ((PaymentResource)resource).PaymentId;
        var ownerId = await _db.Payments
            .AsNoTracking()
            .Where(payment => payment.Id == paymentId)
            .Select(payment => (int?)payment.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerId is null)
            return PermissionDecision.NotFound;

        var held = actor.BaseRelationships();
        if (actor.UserId == ownerId)
            held |= ResourceRelationship.Self;

        return PermissionDecision.Decide(permission, held, resourceIsPublic: false);
    }
}
