# Club indexer

.NET 10 background application consuming PostgreSQL search-outbox CDC through Kafka and applying club upserts/deletes to Elasticsearch. The host registers search infrastructure, an index bootstrap service, a scoped message processor, and a Kafka consumer.

See the [API README](../../../README.md), [architecture](../../../../docs/ARCHITECTURE.md), [configuration](../../../../docs/CONFIGURATION.md), and [CONTRIBUTING](../../../../CONTRIBUTING.md).

## Dependencies and settings

Provision Kafka, Elasticsearch, and the appropriate PostgreSQL outbox connector: [club-outbox-connector.json](../../../../docker/kafka-connect/club-outbox-connector.json). The worker processes documents carried by messages; normal indexing does not require its own database connection. Compose provisions the database/connector path.

| Variable                  | Default / purpose                                                                          |
| ------------------------- | ------------------------------------------------------------------------------------------ |
| `KAFKA_BOOTSTRAP_SERVERS` | Shared broker setting; standalone fallback `localhost:9092`, template/Compose `kafka:9092` |
| `ELASTICSEARCH_URL`       | Search origin; set to a reachable endpoint                                                 |
| `CLUB_INDEX_TOPIC`        | `club-index-events`                                                                        |
| `CLUB_INDEX_GROUP_ID`     | `club-indexer`                                                                             |
| `CLUB_INDEX_DLQ_TOPIC`    | `club-index-events-dlq`                                                                    |

Options come from the shared environment settings. Non-container runs load ancestor `.env` files; process values take precedence. Topic names must match connector output; group IDs determine offset ownership. See setup for the host-run Kafka listener limitation.

## Run and Docker

From the repository root, with infrastructure and environment settings ready:

```powershell
dotnet run --project backend/src/worker/club-indexer/club-indexer.csproj
docker compose up -d club-indexer
docker compose logs -f club-indexer
```

For a standalone image build, also from the root:

```powershell
docker build -f backend/src/worker/club-indexer/Dockerfile -t eventxperience-club-indexer:local backend
```

Use `backend/` as the build context because the worker shares API types/infrastructure. Compose supplies the network and environment. This is a background consumer, with no HTTP API.

## Processing and failures

`ClubIndexerMessageParser` validates CDC messages and identifies upserts/deletes. `ClubIndexerMessageProcessor` retries search failures up to three times with exponential delay starting at 500 ms. Malformed messages and exhausted indexing failures go to the DLQ.

The consumer starts at the earliest available offset when its group has no committed position and disables auto-commit. It commits after processing returns, including successful DLQ publication. DLQ publication errors propagate; the consumer reconnects after five seconds and can retry the uncommitted message. Shutdown cancellation propagates.

Offsets are not committed atomically with Elasticsearch changes, so delivery can repeat. Monitor lag, search failures, and DLQ contents; investigate and repair failed documents before deliberate replay. Replaying a backlog can affect search state and should follow an operational decision.

## Tests

From the repository root:

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~ClubIndexing"
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.Worker"
```

Parser tests use linked sources. Shared worker tests cover selected host/options/publisher behavior; use the backend integration suite for outbox/search persistence flows and the full stack for connector-to-index delivery. See [testing](../../../../docs/TESTING.md) for prerequisites and coverage boundaries.
