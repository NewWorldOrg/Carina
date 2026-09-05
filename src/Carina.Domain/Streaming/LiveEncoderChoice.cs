using Carina.Domain.Machines;

namespace Carina.Domain.Streaming;

public sealed record LiveEncoderChoice
{
    private LiveEncoderChoice(LiveEncoder encoder, CardStanding? fellBackBecause, string note)
    {
        Encoder = encoder;
        FellBackBecause = fellBackBecause;
        Note = note;
    }

    public LiveEncoder Encoder { get; }

    public CardStanding? FellBackBecause { get; }

    public string Note { get; }

    public bool FellBack => FellBackBecause is not null;

    public static LiveEncoderChoice Asked(LiveEncoder encoder)
    {
        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded by one of the two the benchmark compared.");
        }

        return new LiveEncoderChoice(encoder, null, string.Empty);
    }

    public static LiveEncoderChoice FellBackToSoftware(CardStanding because, string note)
    {
        if (CardStandings.IsUsable(because))
        {
            throw new ArgumentOutOfRangeException(
                nameof(because),
                because,
                "A card this machine can encode on is not a reason to fall back to the processor.");
        }

        return new LiveEncoderChoice(LiveEncoder.Software, because, TranscoderNote.Of(note));
    }
}
