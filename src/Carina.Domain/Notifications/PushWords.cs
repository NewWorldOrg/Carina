namespace Carina.Domain.Notifications;

/// <summary>
/// The title and the body of a notice as a browser shows them: the words as they are, without the escapes a chat
/// takes, cut at whole graphemes so that the message stays within what a push service carries.
/// </summary>
public sealed record PushWords(string Title, string Body)
{
    private const int ShareOfTheTitle = 3;

    /// <summary>
    /// The words cut to weigh no more than <paramref name="room"/> together, where <paramref name="weight"/> says
    /// what a piece of text weighs once written. The title takes a third of the room at most, the body what is left.
    /// </summary>
    public static PushWords Within(string title, string body, int room, Func<string, int> weight)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(weight);
        ArgumentOutOfRangeException.ThrowIfNegative(room);

        string cleanTitle = NoticeText.Clean(title);
        string cleanBody = NoticeText.CleanLines(body);

        if (weight(cleanTitle) + weight(cleanBody) <= room)
        {
            return new PushWords(cleanTitle, cleanBody);
        }

        string fittedTitle = Fitted(cleanTitle, Math.Min(weight(cleanTitle), room / ShareOfTheTitle), weight);

        return new PushWords(fittedTitle, Fitted(cleanBody, room - weight(fittedTitle), weight));
    }

    private static string Fitted(string said, int room, Func<string, int> weight)
    {
        if (weight(said) <= room)
        {
            return said;
        }

        int left = room - weight(NoticeText.Ellipsis);

        if (left < 0)
        {
            return string.Empty;
        }

        List<string> kept = [];

        foreach (string grapheme in NoticeText.Graphemes(said))
        {
            left -= weight(grapheme);

            if (left < 0)
            {
                break;
            }

            kept.Add(grapheme);
        }

        return string.Concat(kept) + NoticeText.Ellipsis;
    }
}
