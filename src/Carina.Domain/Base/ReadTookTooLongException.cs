using System.Globalization;

namespace Carina.Domain.Base;

public sealed class ReadTookTooLongException(TimeSpan patience, Exception? cause = null)
    : Exception(Saying(patience), cause)
{
    public TimeSpan Patience { get; } = patience;

    private static string Saying(TimeSpan patience)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"The store was given {patience.TotalSeconds:0.###} seconds to answer this read and took longer.");
}
