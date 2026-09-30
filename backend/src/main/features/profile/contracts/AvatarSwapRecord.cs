namespace backend.main.features.profile.contracts;

/// <param name="User">The user row after the swap.</param>
/// <param name="PreviousAvatar">
/// The avatar URL this swap replaced, read in the same unit of work that wrote the new one. Two
/// uploads racing each other therefore report different previous values, so each deletes the blob
/// it actually replaced and neither leaves one behind.
/// </param>
public sealed record AvatarSwapRecord(User User, string? PreviousAvatar);
