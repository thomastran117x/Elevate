using backend.main.models.core;

namespace backend.main.services.interfaces
{
    public interface IEventSearchOutboxWriter
    {
        void StageUpsert(Events ev);
        void StageDelete(int eventId);
    }
}
