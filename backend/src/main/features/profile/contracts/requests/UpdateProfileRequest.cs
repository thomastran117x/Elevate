using System.ComponentModel.DataAnnotations;

namespace backend.main.features.profile.contracts.requests
{
    public class UpdateProfileRequest
    {
        [StringLength(100)]
        public string? Name { get; set; }

        [StringLength(50, MinimumLength = 1)]
        public string? Username { get; set; }

        [Url]
        [StringLength(500)]
        public string? Avatar { get; set; }
    }
}
