using System.Text.Json;

using backend.main.infrastructure.database.core;

namespace backend.main.features.clubs.search
{
    public class ClubSearchOutboxWriter : IClubSearchOutboxWriter
    {
        private const string ClubAggregateType = "club-index";
        private const string UpsertType = "upsert";
        private const string DeleteType = "delete";

        private readonly AppDatabaseContext _context;

        public ClubSearchOutboxWriter(AppDatabaseContext context)
        {
            _context = context;
        }

        public void StageUpsert(Club club)
        {
            _context.ClubSearchOutbox.Add(new ClubSearchOutbox
            {
                AggregateType = ClubAggregateType,
                AggregateId = club.Id.ToString(),
                Type = UpsertType,
                Payload = JsonSerializer.Serialize(ClubSearchDocumentMapper.ToDocument(club))
            });
        }

        public void StageDelete(int clubId)
        {
            _context.ClubSearchOutbox.Add(new ClubSearchOutbox
            {
                AggregateType = ClubAggregateType,
                AggregateId = clubId.ToString(),
                Type = DeleteType,
                Payload = JsonSerializer.Serialize(new ClubSearchDeletePayload(clubId))
            });
        }
    }
}
