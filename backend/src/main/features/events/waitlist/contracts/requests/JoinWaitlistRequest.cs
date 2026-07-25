using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.waitlist.contracts.requests
{
    /// <summary>
    /// Optional attendee details captured at join time and carried onto the registration
    /// when the entry is promoted, so a promoted user does not have to re-enter them.
    /// </summary>
    public class JoinWaitlistRequest
    {
        [StringLength(500)]
        public string? Notes
        {
            get; set;
        }

        [StringLength(32)]
        public string? PhoneNumber
        {
            get; set;
        }

        [StringLength(500)]
        public string? DietaryNeeds
        {
            get; set;
        }
    }
}
