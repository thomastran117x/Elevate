using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace backend.main.features.clubs.posts.comments.contracts.requests;

public sealed class PostCommentReactionRequest
{
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PostCommentReactionType? Reaction
    {
        get; set;
    }
}
