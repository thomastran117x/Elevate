namespace backend.main.features.events.invitations;

public enum EventInvitationResolveState
{
    LoginRequired = 0,
    AcceptAvailable = 1,
    AlreadyAccepted = 2,
    Declined = 3,
    Expired = 4,
    Revoked = 5,
    Invalid = 6
}
