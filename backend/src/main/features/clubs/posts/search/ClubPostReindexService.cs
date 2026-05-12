using backend.main.shared.exceptions.http;
using backend.main.features.clubs.posts;
using backend.main.shared.utilities.logger;

namespace backend.main.features.clubs.posts.search
{
    public class ClubPostReindexService : IClubPostReindexService
    {
        private const int BatchSize = 100;
        private static readonly TimeSpan ReindexTimeout = TimeSpan.FromMinutes(10);

        private readonly IClubPostRepository _postRepository;
        private readonly IClubPostSearchService _searchService;

        public ClubPostReindexService(IClubPostRepository postRepository, IClubPostSearchService searchService)
        {
            _postRepository = postRepository;
            _searchService = searchService;
        }

        public async Task<int> ReindexAllAsync(CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ReindexTimeout);
            var token = timeoutCts.Token;

            try
            {
                token.ThrowIfCancellationRequested();
                await _searchService.DeleteIndexAsync(token);
                await _searchService.EnsureIndexAsync(token);

                int totalIndexed = 0;
                int page = 1;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    var posts = await _postRepository.GetAllForReindexAsync(page, BatchSize, token);
                    if (posts.Count == 0) break;

                    var documents = posts.Select(p => new ClubPostDocument
                    {
                        Id = p.Id,
                        ClubId = p.ClubId,
                        UserId = p.UserId,
                        Title = p.Title,
                        Content = p.Content,
                        PostType = p.PostType.ToString(),
                        LikesCount = p.LikesCount,
                        IsPinned = p.IsPinned,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt
                    });

                    await _searchService.BulkIndexAsync(documents, token);
                    totalIndexed += posts.Count;
                    page++;

                    if (posts.Count < BatchSize) break;
                }

                Logger.Info($"Reindex complete. {totalIndexed} posts indexed.");
                return totalIndexed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Warn($"Club post reindex exceeded the {ReindexTimeout.TotalMinutes:0} minute timeout.");
                throw new GatewayTimeoutException("Club post reindex timed out.");
            }
        }
    }
}
