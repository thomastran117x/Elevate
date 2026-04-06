using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.postcomment
{
    public class PostCommentCreateRequest
    {
        [Required]
        [StringLength(1000)]
        public string Content { get; set; } = string.Empty;
    }
}
