namespace backend.main.shared.storage
{
    /// <summary>
    /// Limits applied to images that arrive through the presigned upload flow.
    /// </summary>
    public sealed class ImageUploadOptions
    {
        /// <summary>
        /// Largest image accepted when a blob is attached. A SAS cannot bound the size of an
        /// upload — <c>BlobSasBuilder</c> has no content-length field — so the cap is enforced at
        /// attach time instead, the first server-side moment where the bytes exist and we hold a
        /// handle to them.
        /// </summary>
        public long MaxBytes { get; set; } = 5 * 1024 * 1024;
    }
}
