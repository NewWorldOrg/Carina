namespace Carina.Domain.Encodings;

public sealed record ConstantRateFactor
{
    public const int Finest = 0;

    public const int Coarsest = 51;

    public ConstantRateFactor(int rateFactor)
    {
        if (rateFactor is < Finest or > Coarsest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rateFactor),
                rateFactor,
                $"A rate factor lies between {Finest} and {Coarsest}.");
        }

        RateFactor = rateFactor;
    }

    public int RateFactor { get; }

    public override string ToString() => $"crf{RateFactor}";
}

public sealed record ConstantQuantiser
{
    public const int Finest = 0;

    public const int Coarsest = 51;

    public ConstantQuantiser(int quantiser)
    {
        if (quantiser is < Finest or > Coarsest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantiser),
                quantiser,
                $"A quantiser lies between {Finest} and {Coarsest}.");
        }

        Quantiser = quantiser;
    }

    public int Quantiser { get; }

    public override string ToString() => $"qp{Quantiser}";
}
