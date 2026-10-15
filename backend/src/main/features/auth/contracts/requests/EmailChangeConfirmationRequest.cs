namespace backend.main.features.auth.contracts.requests
{
    /// <summary>
    /// Confirms an email change by either proof: the token from the emailed link, or the code and
    /// challenge from the page that requested it.
    /// </summary>
    public sealed class EmailChangeConfirmationRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        public string? Token
        {
            get; set;
        }
        [RequestText(RequestTextKind.Sensitive)]
        public string? Code
        {
            get; set;
        }
        [RequestText(RequestTextKind.Sensitive)]
        public string? Challenge
        {
            get; set;
        }
    }
}
