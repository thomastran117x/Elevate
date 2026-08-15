namespace backend.main.features.profile;

public sealed class UsernameReservation
{
    public required string Username { get; set; }
    public int UserId { get; set; }
    public DateTime ReservedUntilUtc { get; set; }
}
