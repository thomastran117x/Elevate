using backend.main.features.clubs.contracts.responses;

namespace backend.main.features.clubs.versions.contracts.responses;

public sealed record ClubRollbackResponse(
    ClubResponse Club,
    int RestoredFromVersionNumber,
    int NewVersionNumber);
