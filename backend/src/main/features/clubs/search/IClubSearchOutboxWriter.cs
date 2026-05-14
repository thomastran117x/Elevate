namespace backend.main.features.clubs.search
{
    public interface IClubSearchOutboxWriter
    {
        void StageUpsert(Club club);
        void StageDelete(int clubId);
    }
}
