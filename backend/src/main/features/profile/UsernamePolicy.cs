using backend.main.shared.exceptions.http;

namespace backend.main.features.profile;

public static class UsernamePolicy
{
    public const int MaxLength = 50;

    public static string Normalize(string? username) =>
        (username ?? string.Empty).Trim().ToLowerInvariant();

    public static string NormalizeAndValidate(string? username)
    {
        var normalized = Normalize(username);
        if (normalized.Length == 0)
            throw new BadRequestException("Username is required.");

        if (normalized.Length > MaxLength)
            throw new BadRequestException($"Username must be {MaxLength} characters or fewer.");

        return normalized;
    }
}
