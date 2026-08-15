using System.ComponentModel.DataAnnotations;

namespace backend.main.features.profile;

public sealed class ProfileOptions
{
    [Range(1, 3650)]
    public int UsernameChangeCooldownDays { get; set; } = 30;
}
