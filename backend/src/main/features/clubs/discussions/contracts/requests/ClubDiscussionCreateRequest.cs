using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.discussions.contracts.requests
{
    public class ClubDiscussionCreateRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [RequestText(RequestTextKind.HumanMultiline)]
        [Required]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;
    }
}
