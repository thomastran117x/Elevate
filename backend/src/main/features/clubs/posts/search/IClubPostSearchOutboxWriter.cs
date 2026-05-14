namespace backend.main.features.clubs.posts.search
{
    public interface IClubPostSearchOutboxWriter
    {
        void StageUpsert(ClubPost post);
        void StageDelete(int postId);
    }
}
