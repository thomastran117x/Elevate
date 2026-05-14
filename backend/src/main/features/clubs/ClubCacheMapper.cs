using backend.main.features.clubs.contracts;

namespace backend.main.features.clubs
{
    public static class ClubCacheMapper
    {
        public static ClubCacheDto ToDto(Club club) => new(
            club.Id,
            club.Name,
            club.Description,
            club.Clubtype.ToString(),
            club.ClubImage,
            club.Phone,
            club.Email,
            club.Rating,
            club.WebsiteUrl,
            club.Location,
            club.MemberCount,
            club.EventCount,
            club.AvaliableEventCount,
            club.MaxMemberCount,
            club.isPrivate,
            club.UserId,
            club.CurrentVersionNumber
        );

        public static Club ToEntity(ClubCacheDto dto) => new()
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
            Clubtype = Enum.Parse<ClubType>(dto.Clubtype, true),
            ClubImage = dto.ClubImage,
            Phone = dto.Phone,
            Email = dto.Email,
            Rating = dto.Rating,
            WebsiteUrl = dto.WebsiteUrl,
            Location = dto.Location,
            MemberCount = dto.MemberCount,
            EventCount = dto.EventCount,
            AvaliableEventCount = dto.AvaliableEventCount,
            MaxMemberCount = dto.MaxMemberCount,
            isPrivate = dto.isPrivate,
            UserId = dto.UserId,
            CurrentVersionNumber = dto.CurrentVersionNumber,
        };
    }
}


