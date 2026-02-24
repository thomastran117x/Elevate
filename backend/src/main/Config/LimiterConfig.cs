namespace backend.main.Config
{
    public enum RateLimitStrategy
    {
        FixedWindow,
        TokenBucket
    }

    public class RateLimitOptions
    {
        public RateLimitStrategy Strategy
        {
            get; set;
        }

        public int PermitLimit { get; set; } = 50;
        public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(30);

        public int TokenLimit { get; set; } = 20;
        public int TokensPerPeriod { get; set; } = 20;
        public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(10);
    }
}
