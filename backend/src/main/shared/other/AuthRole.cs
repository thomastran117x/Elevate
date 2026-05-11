using backend.main.exceptions.http;

namespace backend.main.shared.other
{
    public static class AuthRoles
    {
        public const string Admin = "Admin";
        public const string Organizer = "Organizer";
        public const string Participant = "Participant";
        public const string Volunteer = "Volunteer";

        public const string DefaultOAuthRole = Participant;

        public static readonly string[] SignUpRoles = [Participant, Organizer, Volunteer];
        public static readonly string[] AllRoles = [Admin, Organizer, Participant, Volunteer];

        public static bool TryNormalize(string? role, out string normalizedRole)
        {
            normalizedRole = string.Empty;

            if (string.IsNullOrWhiteSpace(role))
                return false;

            normalizedRole = role.Trim().ToLowerInvariant() switch
            {
                "admin" => Admin,
                "organizer" => Organizer,
                "participant" => Participant,
                "volunteer" => Volunteer,
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(normalizedRole);
        }

        public static string NormalizeOrThrow(string? role)
        {
            if (TryNormalize(role, out var normalizedRole))
                return normalizedRole;

            throw new BadRequestException(
                $"Role must be one of: {string.Join(", ", SignUpRoles)}."
            );
        }

        public static string NormalizeStored(string? role)
        {
            if (TryNormalize(role, out var normalizedRole))
                return normalizedRole;

            return role?.Trim() ?? string.Empty;
        }

        public static bool IsKnownRole(string? role) => TryNormalize(role, out _);
    }
}
