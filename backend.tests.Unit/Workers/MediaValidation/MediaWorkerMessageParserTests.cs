using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.worker.media_worker;

using FluentAssertions;

namespace backend.tests.Unit.Workers.MediaValidation;

public class MediaWorkerMessageParserTests
{
    [Fact]
    public void Parse_ShouldReturnTheRequest_WhenEveryFieldIsPresent()
    {
        var request = MediaWorkerTestMessages.Request(attempt: 3, subject: "Club images");

        var parsed = MediaWorkerMessageParser.Parse(MediaWorkerTestMessages.Envelope(request));

        parsed.MediaAssetId.Should().Be(request.MediaAssetId);
        parsed.Attempt.Should().Be(3);
        parsed.QuarantineBlobPath.Should().Be(request.QuarantineBlobPath);
        parsed.PublicUrl.Should().Be(request.PublicUrl);
        parsed.DeclaredContentType.Should().Be("image/png");
        parsed.Subject.Should().Be("Club images");
    }

    [Theory]
    [InlineData("", "*empty*")]
    [InlineData("   ", "*empty*")]
    [InlineData("{not json", "*not valid JSON*")]
    [InlineData("null", "*could not be deserialized*")]
    public void Parse_ShouldRefuseAPayloadThatIsNotARequest(string payload, string message)
    {
        var act = () => MediaWorkerMessageParser.Parse(MediaWorkerTestMessages.Envelope(payload));

        act.Should().Throw<MediaWorkerMessageParseException>().WithMessage(message);
    }

    public static TheoryData<string, string> IncompleteRequests() => new()
    {
        { Serialize(r => r with { MediaAssetId = Guid.Empty }), "*no media asset id*" },
        { Serialize(r => r with { Attempt = 0 }), "*no claim attempt*" },
        { Serialize(r => r with { QuarantineBlobPath = " " }), "*no quarantine blob path*" },
        { Serialize(r => r with { PublicUrl = "" }), "*no public URL*" },
        { Serialize(r => r with { Subject = "" }), "*no subject*" }
    };

    [Theory]
    [MemberData(nameof(IncompleteRequests))]
    public void Parse_ShouldRefuseARequestMissingWhatThePipelineNeeds(string payload, string message)
    {
        var act = () => MediaWorkerMessageParser.Parse(MediaWorkerTestMessages.Envelope(payload));

        act.Should().Throw<MediaWorkerMessageParseException>().WithMessage(message);
    }

    private static string Serialize(Func<RequestShape, RequestShape> change)
    {
        var request = MediaWorkerTestMessages.Request();
        var shape = change(new RequestShape(
            request.MediaAssetId, request.Attempt, request.QuarantineBlobPath, request.PublicUrl, request.Subject));

        return JsonSerializer.Serialize(new MediaValidationRequestMessage
        {
            MediaAssetId = shape.MediaAssetId,
            Attempt = shape.Attempt,
            QuarantineBlobPath = shape.QuarantineBlobPath,
            PublicUrl = shape.PublicUrl,
            DeclaredContentType = request.DeclaredContentType,
            Subject = shape.Subject
        }, JsonOptions.Default);
    }

    public sealed record RequestShape(
        Guid MediaAssetId,
        int Attempt,
        string QuarantineBlobPath,
        string PublicUrl,
        string Subject);
}
