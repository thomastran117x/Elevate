using backend.main.features.clubs;

namespace backend.main.features.clubs.versions;

public sealed record ClubRollbackResult(
    Club Club,
    int RestoredFromVersionNumber,
    int NewVersionNumber);
