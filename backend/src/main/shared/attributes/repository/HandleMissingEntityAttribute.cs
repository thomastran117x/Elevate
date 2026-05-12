namespace backend.main.shared.attributes.repository
{
    /// When applied to a repository method, update/delete on a non-existent or already-deleted
    /// entity is handled gracefully instead of throwing. Catches DbUpdateConcurrencyException
    /// (missing row) and DbUpdateException (e.g. FK constraint when a referenced entity was deleted).
    ///
    /// Intended for saga patterns and compensating actions where the entity may already be gone.
    /// Apply on the interface or implementation method that performs the update or delete.
    ///
    /// When the exception is caught, the proxy returns:
    ///   - Task&lt;bool&gt;  → false
    ///   - Task&lt;T?&gt;   → null / default
    ///   - Task (void)    → completed successfully
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
    public sealed class HandleMissingEntityAttribute : Attribute;
}
