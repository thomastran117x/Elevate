namespace backend.main.features.auth.contracts.responses
{
    /// <summary>
    /// One entry of the caller's account security history. Classification fields are snake_case
    /// names from closed sets, never ordinals; contact details and raw addresses are never included.
    /// </summary>
    public class AccountSecurityEventResponse
    {
        public long Id
        {
            get; set;
        }
        /// <summary>What happened, e.g. <c>signed_in</c> or <c>password_changed</c>.</summary>
        public required string Kind
        {
            get; set;
        }
        public DateTime OccurredAt
        {
            get; set;
        }
        /// <summary>The sign-in method, e.g. <c>password</c> or <c>google</c>, when relevant.</summary>
        public string? Method
        {
            get; set;
        }
        /// <summary>The factor a change was about, e.g. <c>sms</c> or <c>totp</c>, when relevant.</summary>
        public string? Factor
        {
            get; set;
        }
        /// <summary>Why it happened, e.g. <c>password_changed</c> for a revocation, when relevant.</summary>
        public string? Reason
        {
            get; set;
        }
        /// <summary>The client family, e.g. <c>chrome</c>, or <c>unknown</c>.</summary>
        public required string Client
        {
            get; set;
        }
        /// <summary>The device class, e.g. <c>desktop</c>, or <c>unknown</c>.</summary>
        public required string DeviceClass
        {
            get; set;
        }
        /// <summary>The source network prefix, e.g. <c>203.0.113.0/24</c>, never the full address.</summary>
        public string? Network
        {
            get; set;
        }
        public bool InitiatedByAdmin
        {
            get; set;
        }
        /// <summary>
        /// The id of the request that produced the entry. Quoting it lets support find the related
        /// records without the user ever seeing them.
        /// </summary>
        public string? Reference
        {
            get; set;
        }
    }
}
