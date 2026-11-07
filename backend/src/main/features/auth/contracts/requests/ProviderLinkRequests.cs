using System.ComponentModel.DataAnnotations;

using backend.main.shared.attributes.validation;

namespace backend.main.features.auth.contracts.requests
{
    /// <summary>
    /// What the provider callback returned for a link started by link-start. Google returns a
    /// <see cref="Code"/> the server redeems; for Microsoft the browser redeems the code and sends
    /// the <see cref="IdToken"/>.
    /// </summary>
    public sealed class CompleteProviderLinkRequest
    {
        /// <summary>The OAuth <c>state</c> link-start issued.</summary>
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        [MaxLength(128)]
        public required string State
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [MaxLength(4096)]
        public string? Code
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [MaxLength(16384)]
        public string? IdToken
        {
            get; set;
        }
    }

    /// <summary>Confirms the link a provider sign-in proposed after matching this account by email.</summary>
    public sealed class ConfirmProviderLinkRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        [MaxLength(128)]
        public required string LinkToken
        {
            get; set;
        }
    }
}
