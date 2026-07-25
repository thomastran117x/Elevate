namespace backend.main.features.events.waitlist.contracts.responses
{
    public class MyWaitlistStatusResponse
    {
        public bool OnWaitlist
        {
            get; set;
        }
        public int? EntryId
        {
            get; set;
        }

        /// <summary>1-based place in the queue; null when not waiting.</summary>
        public int? Position
        {
            get; set;
        }
        public DateTime? JoinedAtUtc
        {
            get; set;
        }

        /// <summary>Total number of users currently waiting for this event.</summary>
        public int WaitlistCount
        {
            get; set;
        }
    }
}
