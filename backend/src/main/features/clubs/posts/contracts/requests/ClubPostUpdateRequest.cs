using System.ComponentModel.DataAnnotations;

using backend.main.features.clubs.posts;

namespace backend.main.features.clubs.posts.contracts.requests
{
    public class ClubPostUpdateRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [RequestText(RequestTextKind.HumanMultiline)]
        [Required]
        [StringLength(2000)]
        public string Content { get; set; } = string.Empty;

        public PostType PostType { get; set; } = PostType.General;

        public bool IsPinned { get; set; } = false;

        /// <summary>Why a moderator changed someone else's post; recorded in the moderation log.</summary>
        [RequestText(RequestTextKind.HumanMultiline)]
        [StringLength(500)]
        public string? ModerationReason
        {
            get; set;
        }
    }
}


