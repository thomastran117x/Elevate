using System.ComponentModel.DataAnnotations;

namespace backend.main.shared.attributes.validation;

public enum RequestTextKind
{
    HumanSingleLine,
    HumanMultiline,
    Structured,
    Sensitive,
}

/// <summary>
/// Classifies strings accepted in JSON request bodies. Human text is normalized before model
/// validation and checked against the application's plain-text and language policies. Structured
/// and sensitive values are deliberately left byte-for-byte unchanged.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RequestTextAttribute(RequestTextKind kind) : ValidationAttribute
{
    public RequestTextKind Kind { get; } = kind;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (Kind is RequestTextKind.Structured or RequestTextKind.Sensitive || value is null)
            return ValidationResult.Success;

        if (value is string text)
            return ValidateText(text, validationContext);

        if (value is IEnumerable<string?> values)
        {
            foreach (var item in values)
            {
                if (item is not null && ValidateText(item, validationContext) is { } failure)
                    return failure;
            }

            return ValidationResult.Success;
        }

        return new ValidationResult(
            $"{nameof(RequestTextAttribute)} supports only strings and string collections.",
            MemberNames(validationContext));
    }

    private ValidationResult? ValidateText(string value, ValidationContext context)
    {
        var violation = ContentTextPolicy.Validate(
            value,
            allowLineBreaks: Kind == RequestTextKind.HumanMultiline);

        return violation switch
        {
            ContentTextViolation.None => ValidationResult.Success,
            ContentTextViolation.ProhibitedLanguage => new ValidationResult(
                "The text contains prohibited language.",
                MemberNames(context)),
            _ => new ValidationResult(
                "The text contains markup or unsupported characters.",
                MemberNames(context)),
        };
    }

    private static string[] MemberNames(ValidationContext context) =>
        context.MemberName is null ? [] : [context.MemberName];
}
