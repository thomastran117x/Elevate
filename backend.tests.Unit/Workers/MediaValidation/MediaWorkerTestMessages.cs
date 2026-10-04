using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.tests.Unit.Features.Media;
using backend.worker.media_worker;

namespace backend.tests.Unit.Workers.MediaValidation;

/// <summary>Requests and envelopes shaped the way the API publishes them.</summary>
internal static class MediaWorkerTestMessages
{
    public const string QuarantinePath = "events/clubs/1/pending/upload.png";

    public static MediaValidationRequestMessage Request(
        Guid? mediaAssetId = null,
        int attempt = 1,
        string quarantinePath = QuarantinePath,
        string subject = "Event images") => new()
        {
            MediaAssetId = mediaAssetId ?? Guid.CreateVersion7(),
            Attempt = attempt,
            QuarantineBlobPath = quarantinePath,
            PublicUrl = $"{InMemoryBlobStore.PublicBase}/events/clubs/1/pending/upload.webp",
            DeclaredContentType = "image/png",
            Subject = subject
        };

    public static MediaWorkerEnvelope Envelope(MediaValidationRequestMessage request, long offset = 1) =>
        Envelope(JsonSerializer.Serialize(request, JsonOptions.Default), request.MediaAssetId.ToString(), offset);

    public static MediaWorkerEnvelope Envelope(string payload, string? key = null, long offset = 1) =>
        new("eventxperience-media-validation", 0, offset, key, payload, null, new Dictionary<string, string?>());
}
