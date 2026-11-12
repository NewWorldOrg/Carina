using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// What a browser is told of a notice, before it is encrypted: the title and the body as they were worded, the path
/// of the screen to open, and the id of the notice. It is kept within what every push service carries by cutting the
/// words, never the path or the id.
/// </summary>
public static class PushPayload
{
    public const int Longest = 3000;

    public const string Root = "/";

    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static byte[] For(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return For(notice.Title, notice.Body, PathOf(notice.Link), notice.Id.Value);
    }

    public static byte[] For(string title, string body, string path, Guid? id)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrEmpty(path);

        int room = Longest - Bytes(new PushWords(string.Empty, string.Empty), path, id).Length;
        byte[] written = Bytes(PushWords.Within(title, body, Math.Max(room, 0), Weight), path, id);

        if (written.Length > Longest)
        {
            throw new ArgumentException($"The path leaves no room for a message of at most {Longest} bytes.", nameof(path));
        }

        return written;
    }

    /// <summary>
    /// The path, the query and the fragment of <paramref name="link"/>, or the root where a notice has no link.
    /// </summary>
    public static string PathOf(string? link)
    {
        if (link is null || !Uri.TryCreate(link, UriKind.Absolute, out Uri? address))
        {
            return Root;
        }

        return $"{address.PathAndQuery}{address.Fragment}";
    }

    private static int Weight(string said) => JsonEncodedText.Encode(said, Written.Encoder).EncodedUtf8Bytes.Length;

    private static byte[] Bytes(PushWords words, string path, Guid? id)
    {
        JsonObject message = new()
        {
            ["title"] = words.Title,
            ["body"] = words.Body,
            ["path"] = path,
            ["id"] = id?.ToString("D"),
        };

        return JsonSerializer.SerializeToUtf8Bytes(message, Written);
    }
}
