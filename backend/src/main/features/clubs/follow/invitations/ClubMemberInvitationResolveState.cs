namespace backend.main.features.clubs.follow.invitations
{
    /// <summary>
    /// Outcome of resolving a club member invitation token (either a specific invite or a shared
    /// link), used to drive the accept page UI.
    /// </summary>
    public enum ClubMemberInvitationResolveState
    {
        /// <summary>Token does not exist (never issued, already used, or TTL lapsed).</summary>
        Invalid,

        /// <summary>Token exists but its expiry has passed.</summary>
        Expired,

        /// <summary>A shared link that the owner has revoked.</summary>
        Revoked,

        /// <summary>A shared link that has reached its redemption cap.</summary>
        RedemptionsExhausted,

        /// <summary>A valid invitation exists but the caller is not authenticated.</summary>
        LoginRequired,

        /// <summary>The authenticated caller is not the invited recipient (specific invites only).</summary>
        NotRecipient,

        /// <summary>The caller is already a member of the club.</summary>
        AlreadyMember,

        /// <summary>The caller may accept (and, for specific invites, decline).</summary>
        AcceptAvailable
    }

    /// <summary>Which mechanism issued a member invitation token.</summary>
    public enum ClubMemberInvitationSource
    {
        /// <summary>A recipient-bound invitation issued by username/email and delivered via email.</summary>
        DirectInvite,

        /// <summary>A shareable, multi-use invite link.</summary>
        Link
    }
}
