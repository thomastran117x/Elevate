namespace backend.main.features.clubs.contracts.requests
{
    public class ClubOwnershipTransferRequest
    {
        public int NewOwnerUserId
        {
            get; set;
        }

        /// <summary>
        /// Optional username or email of the new owner. When provided, the server resolves it to a
        /// user id (takes precedence over <see cref="NewOwnerUserId"/>).
        /// </summary>
        public string? NewOwnerIdentifier
        {
            get; set;
        }
    }
}
