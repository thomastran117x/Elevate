using backend.main.models.documents;
using backend.main.models.search;
using backend.main.services.interfaces;

using FluentAssertions;

using Xunit;

namespace backend.test.Workers;

public class EventIndexerMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ValidUpsert_IndexesDocumentWithoutDlq()
    {
        var search = new FakeEventSearchService();
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.event_indexer.EventIndexerMessageProcessor(search, dlq);

        await processor.ProcessAsync(CreateEnvelope("upsert", ValidUpsertPayload()));

        search.IndexedDocuments.Should().ContainSingle();
        search.DeletedEventIds.Should().BeEmpty();
        dlq.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ValidDelete_DeletesDocumentWithoutDlq()
    {
        var search = new FakeEventSearchService();
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.event_indexer.EventIndexerMessageProcessor(search, dlq);

        await processor.ProcessAsync(CreateEnvelope("delete", """{ "eventId": 88 }"""));

        search.DeletedEventIds.Should().ContainSingle().Which.Should().Be(88);
        search.IndexedDocuments.Should().BeEmpty();
        dlq.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_InvalidPayload_PublishesToDlq()
    {
        var search = new FakeEventSearchService();
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.event_indexer.EventIndexerMessageProcessor(search, dlq);

        await processor.ProcessAsync(CreateEnvelope("upsert", """{ "id": 0 }"""));

        search.IndexedDocuments.Should().BeEmpty();
        dlq.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessAsync_TransientFailure_RetriesBeforeSuccess()
    {
        var search = new FakeEventSearchService { FailuresBeforeIndexSuccess = 2 };
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.event_indexer.EventIndexerMessageProcessor(search, dlq);

        await processor.ProcessAsync(CreateEnvelope("upsert", ValidUpsertPayload()));

        search.IndexAttempts.Should().Be(3);
        search.IndexedDocuments.Should().ContainSingle();
        dlq.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_PermanentFailure_PublishesToDlq()
    {
        var search = new FakeEventSearchService { AlwaysFailIndex = true };
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.event_indexer.EventIndexerMessageProcessor(search, dlq);

        await processor.ProcessAsync(CreateEnvelope("upsert", ValidUpsertPayload()));

        search.IndexAttempts.Should().Be(4);
        dlq.Messages.Should().ContainSingle();
    }

    private static backend.worker.event_indexer.EventIndexerEnvelope CreateEnvelope(
        string operation,
        string payload) =>
        new(
            "event-index-events",
            0,
            12,
            "event-42",
            payload,
            operation,
            new Dictionary<string, string?> { ["eventType"] = operation }
        );

    private static string ValidUpsertPayload() =>
        """
        {
          "id": 42,
          "clubId": 9,
          "name": "Spring Mixer",
          "description": "Networking night",
          "location": "Downtown Hall",
          "isPrivate": false,
          "startTime": "2026-05-02T18:00:00Z",
          "endTime": "2026-05-02T21:00:00Z",
          "createdAt": "2026-05-01T12:00:00Z",
          "updatedAt": "2026-05-01T13:00:00Z",
          "category": "Other",
          "venueName": "Main Venue",
          "city": "Toronto",
          "tags": ["social", "networking"],
          "registrationCount": 17
        }
        """;

    private sealed class FakeEventSearchService : IEventSearchService
    {
        public List<EventDocument> IndexedDocuments { get; } = new();
        public List<int> DeletedEventIds { get; } = new();
        public int IndexAttempts { get; private set; }
        public int FailuresBeforeIndexSuccess { get; set; }
        public bool AlwaysFailIndex { get; set; }

        public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task IndexAsync(EventDocument document, CancellationToken cancellationToken = default)
        {
            IndexAttempts++;

            if (AlwaysFailIndex || FailuresBeforeIndexSuccess > 0)
            {
                if (FailuresBeforeIndexSuccess > 0)
                    FailuresBeforeIndexSuccess--;

                throw new InvalidOperationException("Indexing failed.");
            }

            IndexedDocuments.Add(document);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int eventId, CancellationToken cancellationToken = default)
        {
            DeletedEventIds.Add(eventId);
            return Task.CompletedTask;
        }

        public Task BulkIndexAsync(
            IEnumerable<EventDocument> documents,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<EventSearchResult> SearchAsync(EventSearchCriteria criteria) =>
            Task.FromResult(new EventSearchResult(new List<EventSearchHit>(), 0));
    }

    private sealed class FakeDlqPublisher : backend.worker.event_indexer.IEventIndexerDlqPublisher
    {
        public List<(backend.worker.event_indexer.EventIndexerEnvelope Envelope, string Error)> Messages { get; } = new();

        public Task PublishAsync(
            backend.worker.event_indexer.EventIndexerEnvelope envelope,
            string error,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((envelope, error));
            return Task.CompletedTask;
        }
    }
}
