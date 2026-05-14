using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

namespace backend.main.features.clubs.search
{
    public class ClubReindexService : IClubReindexService
    {
        private const int BatchSize = 100;
        private static readonly TimeSpan ReindexTimeout = TimeSpan.FromMinutes(10);

        private readonly IClubRepository _clubRepository;
        private readonly IClubSearchService _searchService;

        public ClubReindexService(IClubRepository clubRepository, IClubSearchService searchService)
        {
            _clubRepository = clubRepository;
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

                    var clubs = await _clubRepository.GetAllForReindexAsync(page, BatchSize, token);
                    if (clubs.Count == 0)
                        break;

                    await _searchService.BulkIndexAsync(
                        clubs.Select(ClubSearchDocumentMapper.ToDocument),
                        token);

                    totalIndexed += clubs.Count;
                    page++;

                    if (clubs.Count < BatchSize)
                        break;
                }

                Logger.Info($"Reindex complete. {totalIndexed} clubs indexed.");
                return totalIndexed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Warn($"Club reindex exceeded the {ReindexTimeout.TotalMinutes:0} minute timeout.");
                throw new GatewayTimeoutException("Club reindex timed out.");
            }
        }
    }
}
