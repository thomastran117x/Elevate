using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.reviews.contracts.requests
{
    public class ClubReviewUpdateRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [Required]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Range(1, 5)]
        public int Rating
        {
            get; set;
        }

        [RequestText(RequestTextKind.HumanMultiline)]
        [StringLength(500)]
        public string? Comment
        {
            get; set;
        }
    }
}
