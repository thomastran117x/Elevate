using backend.main.features.events;

namespace backend.main.features.events.versions;

public sealed record EventRollbackResult(
    Events Event,
    int RestoredFromVersionNumber,
    int NewVersionNumber);
