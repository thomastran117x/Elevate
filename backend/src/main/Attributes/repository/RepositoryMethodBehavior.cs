namespace backend.main.attributes.repository
{
    /// Result of resolving attributes for a repository method. Used by the proxy to decide retry and missing-entity handling.
    /// Precedence: interface method → implementation method → implementation class → interface type (first found wins).
    public readonly struct RepositoryMethodBehavior
    {
        public bool NoRetry { get; }
        public bool HandleMissingEntity { get; }

        public RepositoryMethodBehavior(bool noRetry, bool handleMissingEntity)
        {
            NoRetry = noRetry;
            HandleMissingEntity = handleMissingEntity;
        }

        public static RepositoryMethodBehavior Default =>
            new(noRetry: false, handleMissingEntity: false);
    }
}
