using backend.main.features.events;

namespace backend.main.features.events.search
{
    public interface IEventSearchOutboxWriter
    {
        void StageUpsert(Events ev);
        void StageSync(Events ev);
        void StageDelete(int eventId);
    }
}
