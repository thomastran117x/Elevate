using backend.main.models.documents;
using backend.main.models.enums;
using backend.main.services.interfaces;

using FluentAssertions;

using Xunit;

namespace backend.test.Workers;

public class ClubPostIndexerMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ValidUpsert_IndexesClubPost()
    {
        var search = new FakeClubPostSearchService();
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.clubpost_indexer.ClubPostIndexerMessageProcessor(search, dlq);

        await processor.ProcessAsync(CreateEnvelope(
            """
            {
              "operation": "upsert",
              "postId": 41,
              "clubId": 9,
              "userId": 12,
              "title": "Spring Update",
              "content": "Doors open at 7",
              "postType": "Announcement",
              "likesCount": 4,
              "isPinned": true,
              "createdAt": "2026-05-01T12:00:00Z",
              "updatedAt": "2026-05-01T13:00:00Z"
            }
            """
        ));

        search.IndexedDocuments.Should().ContainSingle();
        dlq.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_InvalidPayload_PublishesToDlq()
    {
        var search = new FakeClubPostSearchService();
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.clubpost_indexer.ClubPostIndexerMessageProcessor(search, dlq);

        await processor.ProcessAsync(CreateEnvelope("""{ "operation": "upsert", "postId": 0 }"""));

        search.IndexedDocuments.Should().BeEmpty();
        dlq.Messages.Should().ContainSingle();
    }

    private static backend.worker.clubpost_indexer.ClubPostIndexerEnvelope CreateEnvelope(string payload) =>
        new(
            "clubpost-es-index",
            0,
            12,
            "post-41",
            payload,
            null,
            new Dictionary<string, string?>()
        );

    private sealed class FakeClubPostSearchService : IClubPostSearchService
    {
        public List<ClubPostDocument> IndexedDocuments { get; } = new();

        public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(int postId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BulkIndexAsync(IEnumerable<ClubPostDocument> documents, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(List<int> Ids, int TotalCount)> SearchByClubAsync(int clubId, string search, PostSortBy sortBy, int page, int pageSize) =>
            Task.FromResult((new List<int>(), 0));
        public Task<(List<int> Ids, int TotalCount)> SearchAllAsync(string search, PostSortBy sortBy, int page, int pageSize) =>
            Task.FromResult((new List<int>(), 0));

        public Task IndexAsync(ClubPostDocument document, CancellationToken cancellationToken = default)
        {
            IndexedDocuments.Add(document);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDlqPublisher : backend.worker.clubpost_indexer.IClubPostIndexerDlqPublisher
    {
        public List<(backend.worker.clubpost_indexer.ClubPostIndexerEnvelope Envelope, string Error)> Messages { get; } = new();

        public Task PublishAsync(
            backend.worker.clubpost_indexer.ClubPostIndexerEnvelope envelope,
            string error,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((envelope, error));
            return Task.CompletedTask;
        }
    }
}
