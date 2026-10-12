namespace Carina.Driver.Descrambling;

public sealed class DescramblingException(string message) : IOException(message);

public interface IDescrambler : IDisposable
{
    byte[] Descramble(ReadOnlySpan<byte> stream);

    byte[] WhatItCouldNotRead();
}

public interface IDescramblerFactory
{
    bool Unscrambles { get; }

    /// <summary>
    /// The card that answered last, when this driver started or as a tuner opened, or null where none has.
    /// </summary>
    CardIdentity? CardInHand { get; }

    IDescrambler? Open();
}

public sealed class NoDescrambling : IDescramblerFactory
{
    public static readonly NoDescrambling Instance = new();

    private NoDescrambling() { }

    public bool Unscrambles => false;

    public CardIdentity? CardInHand => null;

    public IDescrambler? Open() => null;
}
