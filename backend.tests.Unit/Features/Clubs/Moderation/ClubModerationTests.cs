using backend.main.application.security;
using backend.main.application.security.authorization;
using backend.main.features.clubs.moderation;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Support;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs.Moderation;

public class ClubModerationTests
{
    private const int ClubId = 4;
    private const int OwnerId = 1;
    private const int ManagerId = 2;
    private const int VolunteerId = 3;
    private const int MemberId = 4;
    private const int AdminId = 9;

    // A stand-in with the removal rule's shape, so these helpers are tested apart from the catalog.
    private static readonly Permission Remove = new(
        "test.remove",
        ResourceKind.ClubContent,
        Allowed: ResourceRelationship.Author | ResourceRelationship.SeniorStaff | ResourceRelationship.Admin,
        AllowedWhenPublic: ResourceRelationship.None,
        PermissionDenial.Forbid,
        "Test removal.");

    private static FakeClubAuthorizer Authorizer() => new FakeClubAuthorizer()
        .Club(ClubId, ownerId: OwnerId)
        .Manager(ClubId, ManagerId)
        .Volunteer(ClubId, VolunteerId)
        .Member(ClubId, MemberId);

    private static ClubContentResource ContentBy(int authorId) =>
        new(ClubId, ClubModerationContentTypes.Comment, 30, authorId);

    [Fact]
    public async Task AuthorizeContentAsync_ShouldNotTreatTheAuthorAsModerating()
    {
        var decision = await Authorizer().AuthorizeContentAsync(
            ResourceActor.User(MemberId, AuthRoles.Participant), Remove, ContentBy(MemberId));

        decision.IsModeration.Should().BeFalse();
    }

    [Theory]
    [InlineData(OwnerId, ClubModerationRoles.Owner)]
    [InlineData(ManagerId, ClubModerationRoles.Manager)]
    [InlineData(VolunteerId, ClubModerationRoles.Volunteer)]
    public async Task AuthorizeContentAsync_ShouldReportTheModeratorsClubRole(int actorId, string role)
    {
        var decision = await Authorizer().AuthorizeContentAsync(
            ResourceActor.User(actorId, AuthRoles.Participant), Remove, ContentBy(MemberId));

        decision.Should().Be(new ModerationDecision(true, role));
    }

    [Fact]
    public async Task AuthorizeContentAsync_ShouldReportAdminsWithoutAClubRole()
    {
        var decision = await Authorizer().AuthorizeContentAsync(
            ResourceActor.User(AdminId, AuthRoles.Admin), Remove, ContentBy(OwnerId));

        decision.Should().Be(new ModerationDecision(true, ClubModerationRoles.Admin));
    }

    [Fact]
    public async Task AuthorizeContentAsync_ShouldRefuseStaffWhoDoNotOutrankTheAuthor()
    {
        var act = () => Authorizer().AuthorizeContentAsync(
            ResourceActor.User(VolunteerId, AuthRoles.Volunteer),
            Remove,
            ContentBy(ManagerId),
            new PermissionMessages("You are not allowed to delete this comment."));

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to delete this comment.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireReason_ShouldRejectAMissingReason(string? reason)
    {
        var act = () => ClubModeration.RequireReason(reason);

        act.Should().Throw<BadRequestException>().WithMessage(ClubModeration.ReasonRequiredMessage);
    }

    [Fact]
    public void RequireReason_ShouldTrim()
    {
        ClubModeration.RequireReason("  Spam  ").Should().Be("Spam");
    }

    [Fact]
    public void OptionalReason_ShouldReturnNullWhenBlank_AndRejectOverlongReasons()
    {
        ClubModeration.OptionalReason("  ").Should().BeNull();

        var act = () => ClubModeration.OptionalReason(new string('x', ClubModeration.ReasonMaxLength + 1));

        act.Should().Throw<BadRequestException>().WithMessage(ClubModeration.ReasonTooLongMessage);
    }

    [Fact]
    public void OptionalReason_ShouldApplyThePlainTextPolicy_AndNormalizeLineEndings()
    {
        ClubModeration.OptionalReason(" Spam\r\nRepeated ").Should().Be("Spam\nRepeated");

        var act = () => ClubModeration.OptionalReason("<b>Spam</b>");

        act.Should().Throw<BadRequestException>().WithMessage(ClubModeration.ReasonInvalidMessage);
    }

    [Fact]
    public void Entry_ShouldRecordTheActionAndTruncateTheSnapshot()
    {
        var content = ContentBy(MemberId);
        var entry = ClubModeration.Entry(
            content,
            ManagerId,
            new ModerationDecision(true, ClubModerationRoles.Manager),
            ClubModerationActionTypes.Remove,
            new string('a', ClubModeration.SnapshotMaxLength + 50),
            "Off topic");

        entry.ClubId.Should().Be(ClubId);
        entry.ActorUserId.Should().Be(ManagerId);
        entry.ActorClubRole.Should().Be(ClubModerationRoles.Manager);
        entry.Action.Should().Be(ClubModerationActionTypes.Remove);
        entry.ContentType.Should().Be(ClubModerationContentTypes.Comment);
        entry.ContentId.Should().Be(content.ContentId);
        entry.AuthorUserId.Should().Be(MemberId);
        entry.Reason.Should().Be("Off topic");
        entry.ContentSnapshot.Should().HaveLength(ClubModeration.SnapshotMaxLength + 1).And.EndWith("…");
    }

    [Fact]
    public void Entry_ShouldNotSplitASurrogatePairWhenTruncating()
    {
        // The emoji's two UTF-16 units straddle the cut: positions 199 and 200.
        var text = new string('a', ClubModeration.SnapshotMaxLength - 1) + "😀" + "tail";

        var entry = ClubModeration.Entry(
            ContentBy(MemberId),
            ManagerId,
            new ModerationDecision(true, ClubModerationRoles.Manager),
            ClubModerationActionTypes.Remove,
            text,
            "Spam");

        entry.ContentSnapshot.Should().Be(new string('a', ClubModeration.SnapshotMaxLength - 1) + "…");
        var strictUtf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var encode = () => strictUtf8.GetBytes(entry.ContentSnapshot!);
        encode.Should().NotThrow();
    }

    [Fact]
    public void Entry_ShouldKeepAWholeSurrogatePairThatEndsAtTheCut()
    {
        // The emoji fills positions 198 and 199, so it fits entirely before the cut.
        var text = new string('a', ClubModeration.SnapshotMaxLength - 2) + "😀" + "tail";

        var entry = ClubModeration.Entry(
            ContentBy(MemberId),
            ManagerId,
            new ModerationDecision(true, ClubModerationRoles.Manager),
            ClubModerationActionTypes.Remove,
            text,
            "Spam");

        entry.ContentSnapshot.Should().Be(new string('a', ClubModeration.SnapshotMaxLength - 2) + "😀…");
    }

    [Fact]
    public void Entry_ShouldStoreNoSnapshotForBlankContent()
    {
        var entry = ClubModeration.Entry(
            ContentBy(MemberId),
            ManagerId,
            new ModerationDecision(true, ClubModerationRoles.Manager),
            ClubModerationActionTypes.Remove,
            "  ",
            null);

        entry.ContentSnapshot.Should().BeNull();
    }
}
