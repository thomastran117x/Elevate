using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace backend.main.features.clubs.discussions.replies.contracts.requests;

public sealed class DiscussionReplyCreateRequest
{
    public int? ParentReplyId { get; set; }

    [Required]
    [StringLength(1000)]
    public string Content { get; set; } = string.Empty;
}

public sealed class DiscussionReplyUpdateRequest
{
    [Required]
    [StringLength(1000)]
    public string Content { get; set; } = string.Empty;
}

public sealed class DiscussionReplyReactionRequest
{
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DiscussionReplyReactionType? Reaction { get; set; }
}
