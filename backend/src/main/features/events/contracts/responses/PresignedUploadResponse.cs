namespace backend.main.features.events.contracts.responses
{
    public class PresignedUploadResponse
    {
        public string UploadUrl { get; set; } = null!;
        public string PublicUrl { get; set; } = null!;
        public DateTimeOffset ExpiresAt
        {
            get; set;
        }

        /// <summary>
        /// The media asset tracking this upload, for polling its validation status. Null when
        /// uploads are not quarantined, in which case the image is usable as soon as it is
        /// attached.
        /// </summary>
        public Guid? MediaAssetId
        {
            get; set;
        }
    }
}

