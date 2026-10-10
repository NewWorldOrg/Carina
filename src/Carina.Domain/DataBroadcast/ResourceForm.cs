namespace Carina.Domain.DataBroadcast;

/// <summary>
/// How the bytes of a resource are read: as they came, as UTF-8 text, or as text in a character set left
/// undecoded.
/// </summary>
public enum ResourceForm
{
    Binary = 1,

    Text = 2,

    UndecodedText = 3,
}
