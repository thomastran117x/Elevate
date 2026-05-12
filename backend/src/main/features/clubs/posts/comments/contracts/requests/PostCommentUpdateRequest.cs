using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.posts.comments.contracts.requests
{
    public class PostCommentUpdateRequest
    {
        [Required]
        [StringLength(1000)]
        public string Content { get; set; } = string.Empty;
    }
}
