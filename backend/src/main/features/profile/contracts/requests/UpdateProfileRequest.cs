using System.ComponentModel.DataAnnotations;

namespace backend.main.features.profile.contracts.requests
{
    public class UpdateProfileRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [StringLength(100)]
        public string? Name
        {
            get; set;
        }

        // Avatar is intentionally NOT settable here — it is managed exclusively through the
        // POST /profile/avatar upload pipeline (extension/size validation + blob storage).

        [RequestText(RequestTextKind.Structured)]
        [Phone]
        [StringLength(30)]
        public string? Phone
        {
            get; set;
        }

        [RequestText(RequestTextKind.HumanSingleLine)]
        [StringLength(200)]
        public string? Address
        {
            get; set;
        }
    }
}
