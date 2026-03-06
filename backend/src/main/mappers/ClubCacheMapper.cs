using backend.main.dtos;
using backend.main.models.core;
using backend.main.models.enums;

namespace backend.main.Mappers
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
            club.MemberCount,
            club.EventCount,
            club.AvaliableEventCount,
            club.MaxMemberCount,
            club.isPrivate,
            club.UserId
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
            MemberCount = dto.MemberCount,
            EventCount = dto.EventCount,
            AvaliableEventCount = dto.AvaliableEventCount,
            MaxMemberCount = dto.MaxMemberCount,
            isPrivate = dto.isPrivate,
            UserId = dto.UserId,
        };
    }
}
