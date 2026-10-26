using Carina.Domain.Playback;
using Carina.Domain.Streaming;

using Microsoft.Extensions.Primitives;

namespace Carina.Api.Playback;

public enum DecodingAnswer
{
    Unasked = 1,

    Named = 2,

    NotOneOfThese = 3,
}

/// <summary>
/// The picture codings a browser says it decodes, read from the query it asked with. Naming AV1 is taken at its
/// word and changes nothing, because nothing is encoded in AV1 to be handed over as it is.
/// </summary>
public sealed record AskedDecoding
{
    public const string H264IsCalled = VideoCodecs.H264IsCalled;

    public const string H265IsCalled = VideoCodecs.H265IsCalled;

    public const string Av1IsCalled = VideoCodecs.Av1IsCalled;

    public static readonly IReadOnlyList<string> Names = VideoCodecs.Names;

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
