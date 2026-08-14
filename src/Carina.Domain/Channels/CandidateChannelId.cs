namespace Carina.Domain.Channels;

public sealed class CandidateChannelId : CommonValueObject<Guid>
{
    public CandidateChannelId(Guid value)
        : base(Validated(value))
    {
    }

    public static CandidateChannelId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A candidate channel id cannot be empty.", nameof(value));
        }

        return value;
    }
}
