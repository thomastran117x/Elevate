namespace backend.main.features.clubs.versions.contracts.responses;

public sealed record ClubVersionFieldChangeResponse(
    string Field,
    string? OldValue,
    string? NewValue);
