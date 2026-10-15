using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests;

public sealed class EmailAvailabilityRequest
{
    [RequestText(RequestTextKind.Structured)]
    [Required]
    [EmailAddress]
    public required string Email
    {
        get; set;
    }

    [RequestText(RequestTextKind.Sensitive)]
    public string? Captcha
    {
        get; set;
    }
}
