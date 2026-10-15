using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.registration.contracts.requests
{
    public class UpdateRegistrationRequest
    {
        [RequestText(RequestTextKind.HumanMultiline)]
        [MaxLength(500)]
        public string? Notes
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [MaxLength(30)]
        public string? PhoneNumber
        {
            get; set;
        }

        [RequestText(RequestTextKind.HumanMultiline)]
        [MaxLength(250)]
        public string? DietaryNeeds
        {
            get; set;
        }
    }
}
