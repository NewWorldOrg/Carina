namespace Carina.Domain.Streaming;

public interface ITranscodeBudget
{
    /// <summary>
    /// How many transcoders are up at this moment, live and playback together. Anything above none
    /// means a picture is being made for someone watching, which is what the encode queue gives way
    /// to before it starts another job.
    /// </summary>
    int Running { get; }

    TranscodeClaim Claim(TranscodePurpose purpose);
}

public interface ITranscodeSeat : IDisposable
{
    TranscodePurpose Purpose { get; }

    int Place { get; }

    int AtOnce { get; }
}
