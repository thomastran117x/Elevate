namespace backend.main.shared.requests
{
    public class ClientRequestInfo
    {
        public string IpAddress { get; set; } = "Unknown";
        public string DeviceType { get; set; } = "Unknown";
        public string ClientName { get; set; } = "Unknown";
        public bool IsBrowserClient { get; set; } = true;
    }
}
