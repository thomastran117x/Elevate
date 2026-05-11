namespace backend.main.features.auth;

public class User
{
    public int Id
    {
        get; set;
    }
    public required string Email
    {
        get; set;
    }
    public string? Password
    {
        get; set;
    }
    public required string Usertype
    {
        get; set;
    }
    public string? Name
    {
        get; set;
    }
    public string? Username
    {
        get; set;
    }
    public string? Avatar
    {
        get; set;
    }
    public string? Address
    {
        get; set;
    }
    public string? Phone
    {
        get; set;
    }
    public string? MicrosoftID
    {
        get; set;
    }
    public string? GoogleID
    {
        get; set;
    }
    public bool IsDisabled
    {
        get; set;
    } = false;
    public DateTime? DisabledAtUtc
    {
        get; set;
    }
    public string? DisabledReason
    {
        get; set;
    }
    public int AuthVersion
    {
        get; set;
    } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
