namespace Carina.Domain.Notifications;

/// <summary>
/// The body of a notice, built one fact a line. A fact with nothing to say takes no line.
/// </summary>
internal sealed class NoticeLines
{
    private const string Separator = " · ";

    private const string Listed = "、";

    private const string Unnamed = "(番組名なし)";

    private readonly List<string> lines = [];

    public static NoticeLines About(string? programmeName)
    {
        string name = NoticeText.Clean(programmeName);

        return new NoticeLines().Line(name.Length is 0 ? Unnamed : name);
    }

    public static NoticeLines About(string? programmeName, string? station, string when)
        => About(programmeName).Line(NoticeText.Clean(station), when);

    public NoticeLines Line(string? said)
    {
        if (!string.IsNullOrWhiteSpace(said))
        {
            lines.Add(said);
        }

        return this;
    }

    public NoticeLines Line(string? first, string second)
        => Line(string.IsNullOrWhiteSpace(first) ? second : $"{first}{Separator}{second}");

    public NoticeLines Fact(string label, string? value)
        => Line(string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value}");

    public NoticeLines Fact(string label, IEnumerable<string> values) => Fact(label, string.Join(Listed, values.Distinct()));

    public string Body() => string.Join('\n', lines);
}
