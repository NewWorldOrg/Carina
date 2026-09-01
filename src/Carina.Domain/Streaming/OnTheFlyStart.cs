namespace Carina.Domain.Streaming;

public enum OnTheFlyRefusal
{
    NothingToPlay = 1,

    TooManyAlready = 2,

    TranscoderWouldNotStart = 3,

    NothingCameOut = 4,

    TookTooLong = 5,
}

public interface IOnTheFlyViewing : IAsyncDisposable
{
    OnTheFlyStanding Standing { get; }

    Stream Output { get; }

    Task<TranscoderExit> Completion { get; }
}

public sealed class OnTheFlyStart
{
    private OnTheFlyStart(IOnTheFlyViewing? viewing, OnTheFlyRefusal? refusal, string note)
    {
        Viewing = viewing;
        Refusal = refusal;
        Note = note;
    }

    public IOnTheFlyViewing? Viewing { get; }

    public OnTheFlyRefusal? Refusal { get; }

    public string Note { get; }

    public bool Running => Viewing is not null;

    public static OnTheFlyStart Started(IOnTheFlyViewing viewing)
    {
        ArgumentNullException.ThrowIfNull(viewing);

        return new OnTheFlyStart(viewing, null, string.Empty);
    }

    public static OnTheFlyStart Refused(OnTheFlyRefusal refusal, string note)
    {
        if (!Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A recording is refused for one of the reasons named here.");
        }

        return new OnTheFlyStart(null, refusal, TranscoderNote.Of(note));
    }
}
