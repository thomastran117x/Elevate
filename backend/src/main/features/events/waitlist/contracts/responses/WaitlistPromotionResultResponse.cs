namespace backend.main.features.events.waitlist.contracts.responses
{
    public class WaitlistPromotionResultResponse
    {
        public int PromotedCount
        {
            get; set;
        }
        public List<int> PromotedUserIds { get; set; } = new();
    }
}
