namespace backend.main.features.clubs.search
{
    public static class ClubSearchDocumentMapper
    {
        public static ClubDocument ToDocument(Club club) => new()
        {
            Id = club.Id,
            Name = club.Name,
            Description = club.Description,
            ClubType = club.Clubtype.ToString(),
            Location = club.Location,
            WebsiteUrl = club.WebsiteUrl,
            Phone = club.Phone,
            Email = club.Email,
            MemberCount = club.MemberCount,
            Rating = club.Rating,
            IsPrivate = club.isPrivate,
            CreatedAt = club.CreatedAt,
            UpdatedAt = club.UpdatedAt
        };
    }
}
