using System.ComponentModel.DataAnnotations;

namespace backend.main.features.profile.contracts.requests
{
    public class UpdateProfileRequest
    {
        [StringLength(100)]
        public string? Name
        {
            get; set;
        }

        [StringLength(50, MinimumLength = 1)]
        public string? Username
        {
            get; set;
        }

        // Avatar is intentionally NOT settable here — it is managed exclusively through the
        // POST /profile/avatar upload pipeline (extension/size validation + blob storage).

        [Phone]
        [StringLength(30)]
        public string? Phone
        {
            get; set;
        }

        [StringLength(200)]
        public string? Address
        {
            get; set;
        }
    }
}
