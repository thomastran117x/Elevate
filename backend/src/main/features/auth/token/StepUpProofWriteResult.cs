namespace backend.main.features.auth.token
{
    /// <summary>Outcome of storing a step-up proof against a refresh session.</summary>
    public enum StepUpProofWriteResult
    {
        Stored,

        /// <summary>The refresh session was revoked or expired, so no proof was written.</summary>
        SessionEnded,

        /// <summary>The cache could not be reached, so nothing is known to have been written.</summary>
        Unavailable,
    }
}
