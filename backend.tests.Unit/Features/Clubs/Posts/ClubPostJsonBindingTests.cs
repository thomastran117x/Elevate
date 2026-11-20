using System.Text.Json;

using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.contracts.requests;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs.Posts;

/// <summary>
/// The Angular posts editor sends the post type as its name. There is no global
/// <c>JsonStringEnumConverter</c>, so the request properties opt in; numbers still bind.
/// </summary>
public class ClubPostJsonBindingTests
{
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateAndUpdateRequests_ShouldBindThePostTypeSentAsAName()
    {
        const string json = """{ "title": "Hi", "content": "Body", "postType": "Announcement", "isPinned": true }""";

        JsonSerializer.Deserialize<ClubPostCreateRequest>(json, WebDefaults)!.PostType.Should().Be(PostType.Announcement);
        JsonSerializer.Deserialize<ClubPostUpdateRequest>(json, WebDefaults)!.PostType.Should().Be(PostType.Announcement);
    }

    [Fact]
    public void CreateAndUpdateRequests_ShouldStillBindThePostTypeSentAsANumber()
    {
        const string json = """{ "title": "Hi", "content": "Body", "postType": 2, "isPinned": false }""";

        JsonSerializer.Deserialize<ClubPostCreateRequest>(json, WebDefaults)!.PostType.Should().Be(PostType.Event);
        JsonSerializer.Deserialize<ClubPostUpdateRequest>(json, WebDefaults)!.PostType.Should().Be(PostType.Event);
    }
}
