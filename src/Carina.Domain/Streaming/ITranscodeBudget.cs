namespace Carina.Domain.Streaming;

public interface ITranscodeBudget
{
    int Running { get; }

    TranscodeClaim Claim(TranscodePurpose purpose);
}

public interface ITranscodeSeat : IDisposable
{
    TranscodePurpose Purpose { get; }

    int Place { get; }

    int AtOnce { get; }
}
