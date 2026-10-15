using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.waitlist.contracts.requests
{
    /// <summary>
    /// Optional attendee details captured at join time and carried onto the registration
    /// when the entry is promoted, so a promoted user does not have to re-enter them.
    /// </summary>
    public class JoinWaitlistRequest
    {
        [RequestText(RequestTextKind.HumanMultiline)]
        [StringLength(500)]
        public string? Notes
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [StringLength(32)]
        public string? PhoneNumber
        {
            get; set;
        }

        [RequestText(RequestTextKind.HumanMultiline)]
        [StringLength(500)]
        public string? DietaryNeeds
        {
            get; set;
        }
    }
}
