namespace backend.main.features.clubs.invitations
{
    /// <summary>
    /// Outcome of resolving a club staff invitation token, used to drive the accept page UI.
    /// </summary>
    public enum ClubInvitationResolveState
    {
        /// <summary>Token does not exist (never issued, already used, revoked, or TTL lapsed).</summary>
        Invalid,

        /// <summary>Token exists but its expiry has passed.</summary>
        Expired,

        /// <summary>A valid invitation exists but the caller is not authenticated.</summary>
        LoginRequired,

        /// <summary>The authenticated caller is not the invited recipient.</summary>
        NotRecipient,

        /// <summary>The caller is the recipient and may accept or decline.</summary>
        AcceptAvailable
    }
}
