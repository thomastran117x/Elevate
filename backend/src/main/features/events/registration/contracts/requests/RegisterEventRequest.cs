using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.registration.contracts.requests
{
    public class RegisterEventRequest
    {
        [MaxLength(500)]
        public string? Notes
        {
            get; set;
        }

        [MaxLength(30)]
        public string? PhoneNumber
        {
            get; set;
        }

        [MaxLength(250)]
        public string? DietaryNeeds
        {
            get; set;
        }
    }
}
