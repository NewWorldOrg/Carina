namespace Carina.Domain.Streaming;

public interface ITranscodeBudget
{
    /// <summary>
    /// How many transcoders are up at this moment, live and playback together.
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
