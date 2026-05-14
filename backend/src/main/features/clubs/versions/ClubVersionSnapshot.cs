namespace backend.main.features.clubs.versions;

public sealed class ClubVersionSnapshot
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Clubtype { get; init; }
    public required string ClubImage { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? Location { get; init; }
    public int MaxMemberCount { get; init; }
    public bool IsPrivate { get; init; }
}
