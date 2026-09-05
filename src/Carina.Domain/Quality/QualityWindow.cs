namespace Carina.Domain.Quality;

public enum QualityWindow
{
    Minute = 1,

    Hour = 2,
}

public sealed record LayerErrorRate(int Layer, double Average, double Highest);
