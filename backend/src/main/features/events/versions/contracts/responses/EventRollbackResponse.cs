using backend.main.features.events.contracts.responses;

namespace backend.main.features.events.versions.contracts.responses;

public sealed record EventRollbackResponse(
    EventResponse Event,
    int RestoredFromVersionNumber,
    int NewVersionNumber);
