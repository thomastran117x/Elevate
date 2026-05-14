namespace backend.main.features.clubs.posts.search
{
    public static class ClubPostSearchDocumentMapper
    {
        public static ClubPostDocument ToDocument(ClubPost post) => new()
        {
            Id = post.Id,
            ClubId = post.ClubId,
            UserId = post.UserId,
            Title = post.Title,
            Content = post.Content,
            PostType = post.PostType.ToString(),
            LikesCount = post.LikesCount,
            IsPinned = post.IsPinned,
            CreatedAt = post.CreatedAt,
            UpdatedAt = post.UpdatedAt
        };
    }
}
