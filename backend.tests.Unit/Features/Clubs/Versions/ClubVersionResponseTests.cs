using backend.main.features.clubs.versions.contracts.responses;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs.Versions;

public class ClubVersionResponseTests
{
    [Fact]
    public void SnapshotRecord_ShouldExposeConstructorValues()
    {
        var snapshot = new ClubVersionSnapshotResponse(
            "Chess Club",
            "Strategy nights",
            "Social",
            "https://cdn.test/club.png",
            "555-0100",
            "club@example.com",
            "https://club.example.com",
            "Campus",
            250,
            true);

        snapshot.Name.Should().Be("Chess Club");
        snapshot.IsPrivate.Should().BeTrue();
        snapshot.MaxMemberCount.Should().Be(250);
    }

    [Fact]
    public void DetailRecord_ShouldExposeNestedSnapshot()
    {
        var snapshot = new ClubVersionSnapshotResponse(
            "Chess Club",
            "Strategy nights",
            "Social",
            "https://cdn.test/club.png",
            null,
            null,
            null,
            null,
            250,
            false);

        var detail = new ClubVersionDetailResponse(
            3,
            "Update",
            new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc),
            7,
            "Organizer",
            true,
            new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc),
            1,
            [new ClubVersionFieldChangeResponse("name", "Old", "New")],
            snapshot);

        detail.VersionNumber.Should().Be(3);
        detail.Snapshot.Name.Should().Be("Chess Club");
        detail.ChangedFields.Should().ContainSingle();
    }
}
