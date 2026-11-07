namespace backend.main.shared.providers.messages
{
    public sealed class EmailMessage
    {
        public required EmailMessageType Type
        {
            get; init;
        }
        public required string Email
        {
            get; init;
        }
        public string? Token
        {
            get; init;
        }
        public string? Code
        {
            get; init;
        }
        public string? RecipientName
        {
            get; init;
        }
        public string? Username
        {
            get; init;
        }
        /// <summary>The address an email change is moving to. Set for the EmailChange* types.</summary>
        public string? NewEmail
        {
            get; init;
        }
        public IReadOnlyList<string>? SignInProviders
        {
            get; init;
        }
        public int? EventInvitationId
        {
            get; init;
        }
        /// <summary>Enables /events/{id} deep links in CTAs. Optional; falls back to /events.</summary>
        public int? EventId
        {
            get; init;
        }
        public string? EventName
        {
            get; init;
        }
        public string? ClubName
        {
            get; init;
        }
        public string? ActorName
        {
            get; init;
        }
        public DateTime? EventStartsAtUtc
        {
            get; init;
        }
        /// <summary>
        /// The second factor a <see cref="EmailMessageType.MfaFactorChanged"/> notice is about
        /// ("sms", "totp", or "recovery_codes"). Never carries the phone number, secret, or a code.
        /// </summary>
        public string? MfaFactor
        {
            get; init;
        }
        /// <summary>
        /// What happened to <see cref="MfaFactor"/>: "enabled", "disabled", "removed", or
        /// "regenerated". Recovery codes report "enabled" for a first set and "regenerated" when
        /// a set is replaced.
        /// </summary>
        public string? MfaChange
        {
            get; init;
        }
        /// <summary>
        /// The security history kind a <see cref="EmailMessageType.SecurityAlert"/> is about, e.g.
        /// "password_changed". The SecurityAlert* fields are closed classifications and a masked
        /// network prefix; they never carry an address, session, token, or code.
        /// </summary>
        public string? SecurityAlertKind
        {
            get; init;
        }
        public DateTime? SecurityAlertOccurredAtUtc
        {
            get; init;
        }
        /// <summary>The sign-in method or linked provider, e.g. "google", when the kind has one.</summary>
        public string? SecurityAlertMethod
        {
            get; init;
        }
        /// <summary>The client family, e.g. "chrome".</summary>
        public string? SecurityAlertClient
        {
            get; init;
        }
        /// <summary>The device class, e.g. "desktop".</summary>
        public string? SecurityAlertDeviceClass
        {
            get; init;
        }
        /// <summary>The source network prefix, e.g. "203.0.113.0/24".</summary>
        public string? SecurityAlertNetwork
        {
            get; init;
        }
    }
}
