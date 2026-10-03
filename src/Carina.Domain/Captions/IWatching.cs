namespace Carina.Domain.Captions;

public interface IWatching
{
    /// <summary>
    /// Whether anybody is watching live or a recording through a transcoder at this moment.
    /// </summary>
    bool Anyone { get; }
}
