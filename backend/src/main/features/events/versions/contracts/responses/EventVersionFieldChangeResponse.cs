namespace backend.main.features.events.versions.contracts.responses;

public sealed record EventVersionFieldChangeResponse(
    string Field,
    string? OldValue,
    string? NewValue);
