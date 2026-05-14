namespace backend.main.features.clubs.contracts
{
    public record ClubCacheDto(
        int Id,
        string Name,
        string Description,
        string Clubtype,
        string ClubImage,
        string? Phone,
        string? Email,
        double? Rating,
        string? WebsiteUrl,
        string? Location,
        int MemberCount,
        int EventCount,
        int AvaliableEventCount,
        int MaxMemberCount,
        bool isPrivate,
        int UserId,
        int CurrentVersionNumber
    );
}
