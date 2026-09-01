namespace Carina.Driver.Descrambling;

public sealed class DescramblingException(string message) : IOException(message);

public interface IDescrambler : IDisposable
{
    byte[] Descramble(ReadOnlySpan<byte> stream);

    byte[] WhatItCouldNotRead();
}

public interface IDescramblerFactory
{
    bool CardAnswered { get; }

    IDescrambler? Open();
}

public sealed class NoDescrambling : IDescramblerFactory
{
    public static readonly NoDescrambling Instance = new();

    private NoDescrambling() { }

    public bool CardAnswered => false;

    public IDescrambler? Open() => null;
}
