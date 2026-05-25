using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.invitations.contracts.requests;

public sealed class CreateEventInvitationsRequest : IValidatableObject
{
    public List<int>? UserIds
    {
        get; set;
    }

    public List<string>? Emails
    {
        get; set;
    }

    public DateTime? ExpiresAt
    {
        get; set;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UserIds is { Count: > 100 })
        {
            yield return new ValidationResult(
                "A maximum of 100 user IDs is allowed.",
                new[] { nameof(UserIds) });
        }

        if (Emails is { Count: > 100 })
        {
            yield return new ValidationResult(
                "A maximum of 100 email addresses is allowed.",
                new[] { nameof(Emails) });
        }
    }
}
