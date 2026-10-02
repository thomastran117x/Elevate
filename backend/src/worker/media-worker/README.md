# Media worker

.NET 10 background application that validates quarantined image uploads off the API's request path. It consumes validation requests from Kafka, runs `MediaValidationPipeline`, which checks the bytes, decodes and re-encodes them, and publishes the result to the reserved public URL. It then reports the outcome on a status topic. The host registers the image validation pipeline, a scoped message processor, DLQ and status publishers, and a Kafka consumer.

The worker never connects to PostgreSQL. The API claims each asset before publishing a request, and the API's `MediaValidationStatusConsumer` records the outcome the worker reports. Migrations and the `MediaAssets` schema are therefore the API's alone. This mirrors how the email worker reports invitation delivery status.

See the [API README](../../../README.md), [architecture](../../../../docs/ARCHITECTURE.md#image-uploads), [configuration](../../../../docs/CONFIGURATION.md), and [CONTRIBUTING](../../../../CONTRIBUTING.md).

## Dependencies and settings

You need Kafka and the Azure Storage account the API uses, with both containers provisioned (see [deployment](../../../../docs/DEPLOYMENT.md#blob-storage)). The worker reads the private quarantine container and writes the public one.

| Variable                                  | Default / purpose                                                                           |
| ----------------------------------------- | ------------------------------------------------------------------------------------------- |
| `KAFKA_BOOTSTRAP_SERVERS`                 | Shared broker setting. Standalone fallback `localhost:9092`, template/Compose `kafka:9092`  |
| `MEDIA_VALIDATION_TOPIC`                  | `eventxperience-media-validation`. Requests published by the API                            |
| `MEDIA_VALIDATION_GROUP_ID`               | `media-worker`                                                                              |
| `MEDIA_VALIDATION_DLQ_TOPIC`              | `eventxperience-media-validation-dlq`                                                       |
| `MEDIA_VALIDATION_STATUS_TOPIC`           | `eventxperience-media-validation-status`. Results consumed by the API                       |
| `AZURE_STORAGE_CONNECTION_STRING`         | Storage account. Without it the worker logs a warning and idles                             |
| `AZURE_STORAGE_CONTAINER_NAME`            | Public container that serves images. Without it the worker idles                            |
| `AZURE_STORAGE_QUARANTINE_CONTAINER_NAME` | `event-assets-quarantine`                                                                   |
| `ImageUpload__MaxBytes`                   | Size cap, which must match the API's                                                        |
| `ImageProcessing__*`                      | Decode limits and processing slots, validated at startup like the API's (see configuration) |

Options come from the shared environment settings. Non-container runs load ancestor `.env` files, and process values take precedence. The API only publishes requests when `storage.quarantine.inline` is false. Otherwise it runs the same pipeline inside the attach request, and this worker has nothing to consume.

## Run and Docker

From the repository root, with infrastructure and environment settings ready:

```powershell
dotnet run --project backend/src/worker/media-worker/media-worker.csproj
docker compose up -d media-worker
docker compose logs -f media-worker
```

For a standalone image build, also from the root:

```powershell
docker build -f backend/src/worker/media-worker/Dockerfile -t eventxperience-media-worker:local backend
```

Use `backend/` as the build context, because the worker shares the pipeline and storage types with the API. This is a background consumer with no HTTP API.

## Processing and failures

`MediaWorkerMessageParser` checks that a request carries an asset id, a claim attempt, a quarantine path, a public URL, and a subject. `MediaWorkerMessageProcessor` handles the outcomes like this:

- **The pipeline runs.** It retries a fault up to three times, with an exponential delay starting at 500 ms. A fault here means anything that is not a verdict on the bytes, such as no free processing slot or a storage error.
- **A refused image** is an outcome, not a failure. It is reported like an accepted one.
- **Malformed requests and exhausted retries** go to the DLQ. The asset stays Processing, and the API's `MediaValidationReconciler` re-drives it once the claim is stale.
- **A result that cannot be published** propagates. The offset stays uncommitted, the consumer reconnects after five seconds, and the request runs again.

The consumer starts at the earliest offset when its group has no committed position, and it disables auto-commit. It commits after processing returns, including after a successful DLQ publication.

Delivery is at least once, and replay is safe:

- **The public URL is overwritten with identical bytes.** Re-running a request writes the same re-encoded image to the same URL.
- **Only the API promotes an asset.** It records a result only while the asset is still Processing under the claim the result names, so a duplicate result changes nothing.
- **A late replay doesn't publish anything.** Once the API has recorded the outcome and deleted the quarantined bytes, the pipeline finds nothing to read. It reports "did not complete", and the API ignores that report.

## Tests

From the repository root:

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~MediaValidation"
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.Worker"
```

The unit project references this worker, so `media-worker.dll` is measured by the coverage gate. The shared worker tests drive the consume loop against a mocked consumer. The pipeline's own decode and re-encode behaviour is covered under `Shared/Storage`. See [testing](../../../../docs/TESTING.md) for prerequisites and coverage boundaries.
