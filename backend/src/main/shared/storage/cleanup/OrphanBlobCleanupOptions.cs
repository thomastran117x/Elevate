namespace backend.main.shared.storage.cleanup
{
    public sealed class OrphanBlobCleanupOptions
    {
        /// <summary>When false the sweeper does nothing (default; opt-in per environment).</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>Maximum number of orphaned blobs deleted per run.</summary>
        public int BatchSize { get; set; } = 200;

        /// <summary>
        /// Blobs younger than this are never deleted. Protects the presigned-SAS upload flow,
        /// where a blob is uploaded and its URL persisted moments later.
        /// </summary>
        public int MinAgeHours { get; set; } = 24;

        /// <summary>Blob path prefixes to scan for orphans.</summary>
        public string[] Prefixes { get; set; } = ["users", "clubs", "events"];
    }
}
