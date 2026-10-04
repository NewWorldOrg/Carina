using Carina.Domain.Playback;

using Microsoft.Extensions.Primitives;

namespace Carina.Api.Playback;

public enum DecodingAnswer
{
    Unasked = 1,

    Named = 2,

    NotOneOfThese = 3,
}

/// <summary>
/// The picture codings a browser says it decodes, read from the query it asked with.
/// </summary>
public sealed record AskedDecoding
{
    public const string H264IsCalled = "h264";

    public const string H265IsCalled = "h265";

    public static readonly IReadOnlyList<string> Names = [H264IsCalled, H265IsCalled];

    private AskedDecoding(DecodingAnswer answer, PlaybackAudience audience)
    {
        Answer = answer;
        Audience = audience;
    }

    public DecodingAnswer Answer { get; }

    public PlaybackAudience Audience { get; }

    public static AskedDecoding Read(StringValues asked)
    {
        string[] named = [.. asked.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)];

        if (named.Length is 0)
        {
            return new AskedDecoding(DecodingAnswer.Unasked, PlaybackAudience.BrowserSayingNothing);
        }

        if (named.Any(value => !Names.Contains(value, StringComparer.Ordinal)))
        {
            return new AskedDecoding(DecodingAnswer.NotOneOfThese, PlaybackAudience.BrowserSayingNothing);
        }

        return new AskedDecoding(
            DecodingAnswer.Named,
            PlaybackAudience.Browser(named.Contains(H265IsCalled, StringComparer.Ordinal)));
    }
}
