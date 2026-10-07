using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests;

public sealed class EmailAvailabilityRequest
{
    [Required]
    [EmailAddress]
    public required string Email
    {
        get; set;
    }

    public string? Captcha
    {
        get; set;
    }
}
