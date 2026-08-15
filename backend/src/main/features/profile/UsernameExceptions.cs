using backend.main.shared.exceptions.http;

namespace backend.main.features.profile;

public sealed class UsernameTakenException : AppException
{
    public UsernameTakenException(string username)
        : base(
            $"The username '{username}' is already taken.",
            StatusCodes.Status409Conflict,
            "USERNAME_TAKEN")
    {
    }
}

public sealed class UsernameChangeCooldownException : AppException
{
    public UsernameChangeCooldownException(DateTime availableAtUtc)
        : base(
            $"Username cannot be changed again until {availableAtUtc:O}.",
            StatusCodes.Status409Conflict,
            "USERNAME_CHANGE_COOLDOWN",
            new Dictionary<string, object?>
            {
                ["usernameChangeAvailableAtUtc"] = availableAtUtc,
            })
    {
    }
}
