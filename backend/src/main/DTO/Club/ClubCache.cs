namespace backend.main.DTOs
{
    public record ClubCacheDto(
        int Id,
        string Name,
        string Description,
        string Clubtype,
        string ClubImage,
        string? Phone,
        string? Email,
        int MemberCount,
        int EventCount,
        int AvaliableEventCount,
        int MaxMemberCount,
        bool isPrivate,
        int UserId
    );
}
