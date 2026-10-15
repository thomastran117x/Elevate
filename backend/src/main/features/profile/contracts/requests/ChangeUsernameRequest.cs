using System.ComponentModel.DataAnnotations;

namespace backend.main.features.profile.contracts.requests;

public sealed class ChangeUsernameRequest
{
    [RequestText(RequestTextKind.HumanSingleLine)]
    [Required]
    public required string Username
    {
        get; set;
    }
}
