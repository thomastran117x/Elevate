namespace backend.main.features.clubs.discussions
{
    public interface IClubDiscussionRepository
    {
        Task<ClubDiscussion> CreateAsync(ClubDiscussion discussion);
        Task<List<ClubDiscussion>> GetByClubIdAsync(int clubId, int page, int pageSize);
        Task<int> CountByClubIdAsync(int clubId);
        Task<ClubDiscussion?> GetByIdAsync(int id);
        Task<ClubDiscussion?> UpdateAsync(int id, ClubDiscussion updated);
        Task<bool> DeleteAsync(int id);
    }
}
