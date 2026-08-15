using System.Globalization;
using System.Text;

using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.posts.comments;

internal static class PostCommentCursorCodec
{
    public static string Encode(PostComment comment)
    {
        var value = $"{comment.CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{comment.Id.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static PostCommentCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = decoded.Split(':');
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks || id < 1)
                throw new FormatException();
            return new PostCommentCursor(new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new BadRequestException("The comment cursor is invalid.");
        }
    }
}
