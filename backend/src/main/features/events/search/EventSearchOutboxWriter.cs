using System.Text.Json;

using backend.main.features.events;
using backend.main.infrastructure.database.core;

namespace backend.main.features.events.search
{
    public class EventSearchOutboxWriter : IEventSearchOutboxWriter
    {
        private const string EventAggregateType = "event-index";
        private const string UpsertType = "upsert";
        private const string DeleteType = "delete";

        private readonly AppDatabaseContext _context;

        public EventSearchOutboxWriter(AppDatabaseContext context)
        {
            _context = context;
        }

        public void StageUpsert(Events ev)
        {
            _context.EventSearchOutbox.Add(new EventSearchOutbox
            {
                AggregateType = EventAggregateType,
                AggregateId = ev.Id.ToString(),
                Type = UpsertType,
                Payload = JsonSerializer.Serialize(EventSearchDocumentMapper.ToDocument(ev))
            });
        }

        public void StageDelete(int eventId)
        {
            _context.EventSearchOutbox.Add(new EventSearchOutbox
            {
                AggregateType = EventAggregateType,
                AggregateId = eventId.ToString(),
                Type = DeleteType,
                Payload = JsonSerializer.Serialize(new EventSearchDeletePayload(eventId))
            });
        }
    }
}

