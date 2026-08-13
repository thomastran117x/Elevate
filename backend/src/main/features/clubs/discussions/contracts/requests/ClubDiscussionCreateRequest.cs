using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.discussions.contracts.requests
{
    public class ClubDiscussionCreateRequest
    {
        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;
    }
}
