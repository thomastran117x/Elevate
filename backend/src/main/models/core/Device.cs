namespace backend.main.models.core;

public class Device
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string DeviceType { get; set; }
    public required string ClientName { get; set; }
    public string IpAddress { get; set; } = "Unknown";
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
