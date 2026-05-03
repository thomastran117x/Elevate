using System.Text.Json;

using backend.main.dtos.messages;
using backend.main.Mappers;
using backend.main.models.core;
using backend.main.configurations.resource.database;
using backend.main.services.interfaces;

namespace backend.main.services.implementation
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
