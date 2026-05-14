using System.Text.Json;

using backend.main.infrastructure.database.core;

namespace backend.main.features.clubs.posts.search
{
    public class ClubPostSearchOutboxWriter : IClubPostSearchOutboxWriter
    {
        private const string ClubPostAggregateType = "clubpost-index";
        private const string UpsertType = "upsert";
        private const string DeleteType = "delete";

        private readonly AppDatabaseContext _context;

        public ClubPostSearchOutboxWriter(AppDatabaseContext context)
        {
            _context = context;
        }

        public void StageUpsert(ClubPost post)
        {
            _context.Set<ClubPostSearchOutbox>().Add(new ClubPostSearchOutbox
            {
                AggregateType = ClubPostAggregateType,
                AggregateId = post.Id.ToString(),
                Type = UpsertType,
                Payload = JsonSerializer.Serialize(ClubPostSearchDocumentMapper.ToDocument(post))
            });
        }

        public void StageDelete(int postId)
        {
            _context.Set<ClubPostSearchOutbox>().Add(new ClubPostSearchOutbox
            {
                AggregateType = ClubPostAggregateType,
                AggregateId = postId.ToString(),
                Type = DeleteType,
                Payload = JsonSerializer.Serialize(new ClubPostSearchDeletePayload(postId))
            });
        }
    }
}
