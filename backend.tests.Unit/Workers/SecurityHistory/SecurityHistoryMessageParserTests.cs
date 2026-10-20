using System.Text.Json;
using System.Text.Json.Nodes;

using backend.main.features.auth.history;
using backend.main.shared.providers;
using backend.worker.security_history_worker;

using FluentAssertions;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryMessageParserTests
{
    [Fact]
    public void Parse_ShouldAcceptWhatTheProducerSends()
    {
        var sent = SecurityHistoryWorkerTestSupport.Message();

        var parsed = SecurityHistoryMessageParser.Parse(JsonSerializer.Serialize(sent, JsonOptions.Default));

        parsed.Should().BeEquivalentTo(sent);
    }

    [Fact]
    public void Parse_ShouldAcceptAnEntryWithoutOptionalContext()
    {
        var payload = Payload(json =>
        {
            json.Remove("network");
            json.Remove("correlationId");
            json.Remove("factor");
        });

        SecurityHistoryMessageParser.Parse(payload).Network.Should().BeNull();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Parse_ShouldRejectSomethingThatIsNotAMessage(string payload)
    {
        var parse = () => SecurityHistoryMessageParser.Parse(payload);

        parse.Should().Throw<InvalidSecurityHistoryMessageException>();
    }

    public static TheoryData<string, string, JsonNode?> InvalidFields() => new()
    {
        { "messageId", "MessageId", JsonValue.Create(Guid.Empty) },
        { "userId", "UserId", JsonValue.Create(0) },
        { "kind", "Kind", JsonValue.Create(999) },
        { "client", "Client", JsonValue.Create(999) },
        { "deviceClass", "DeviceClass", JsonValue.Create(999) },
        { "eventName", "EventName", JsonValue.Create("auth.sign_in.failed") },
        { "method", "Method", JsonValue.Create("Mozilla/5.0") },
        { "factor", "Factor", JsonValue.Create("fingerprint") },
        { "reason", "Reason", JsonValue.Create("because") },
        { "network", "Network", JsonValue.Create("203.0.113.50") },
        { "network", "Network", JsonValue.Create("203.0.113.50/24") },
        { "network", "Network", JsonValue.Create("<script>/24") },
        { "correlationId", "CorrelationId", JsonValue.Create("id\nforged log line") },
        { "occurredAt", "OccurredAt", JsonValue.Create(default(DateTimeOffset)) },
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void Parse_ShouldRejectAFieldOutsideTheContract(string field, string error, JsonNode? value)
    {
        var payload = Payload(json => json[field] = value?.DeepClone());

        var parse = () => SecurityHistoryMessageParser.Parse(payload);

        parse.Should().Throw<InvalidSecurityHistoryMessageException>().WithMessage($"{error}*");
    }

    [Fact]
    public void Parse_ShouldRejectAnUnknownKindName()
    {
        var payload = Payload(json => json["kind"] = "SignInFailed");

        var parse = () => SecurityHistoryMessageParser.Parse(payload);

        parse.Should().Throw<InvalidSecurityHistoryMessageException>();
    }

    private static string Payload(Action<JsonObject> change)
    {
        var json = JsonSerializer.SerializeToNode(SecurityHistoryWorkerTestSupport.Message(), JsonOptions.Default)!.AsObject();
        change(json);
        return json.ToJsonString();
    }
}
