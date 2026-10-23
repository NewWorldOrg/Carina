using Carina.Domain.Base;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The one row of the data broadcast settings changed from a screen: where the receiver is, as a postal code and a
/// prefecture, each of which may be left unregistered, with when they last changed. No row is held until somebody
/// changes them.
/// </summary>
public sealed class DataBroadcastSettings
{
    public const int TheOnlyRow = 1;

    private DataBroadcastSettings()
    {
    }

    public int Id { get; private set; }

    public string? PostalCode { get; private set; }

    public int? Prefecture { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public static DataBroadcastSettings Rehydrate(int id, string? postalCode, int? prefecture, DateTime changedAt)
    {
        if (postalCode is not null && !DataBroadcastSettingsStanding.IsPostalCode(postalCode))
        {
            throw new ArgumentOutOfRangeException(nameof(postalCode), postalCode, "A postal code is kept as seven digits.");
        }

        if (!DataBroadcastSettingsStanding.TakesPrefecture(prefecture))
        {
            throw new ArgumentOutOfRangeException(nameof(prefecture), prefecture, "A prefecture is kept as its place from 0 to 49.");
        }

        return new DataBroadcastSettings
        {
            Id = id,
            PostalCode = postalCode,
            Prefecture = prefecture,
            ChangedAt = UtcTimes.Required(changedAt, nameof(changedAt)),
        };
    }
}

/// <summary>
/// Where the receiver is as the settings stand: a postal code of seven digits and a prefecture, either of them none
/// while it is not registered, and when they last changed. The prefecture is its place in the prefecture designation
/// of ARIB STD-B10 annex G, table G-2, counted from 0: 0 and 1 are the east and the west of Hokkaido, 2 to 47 the
/// prefectures from Aomori to Okinawa in the order of their codes, 48 the islands of Tokyo and 49 those of Kagoshima.
/// </summary>
public sealed record DataBroadcastSettingsStanding
{
    public const int PostalCodeLength = 7;

    public const int FirstPrefecture = 0;

    public const int LastPrefecture = 49;

    public static DataBroadcastSettingsStanding Unset { get; } = new();

    public string? PostalCode { get; init; }

    public int? Prefecture { get; init; }

    public DateTime? ChangedAt { get; init; }

    public static DataBroadcastSettingsStanding Over(DataBroadcastSettings? held)
        => held is null
            ? Unset
            : new DataBroadcastSettingsStanding
            {
                PostalCode = held.PostalCode,
                Prefecture = held.Prefecture,
                ChangedAt = held.ChangedAt,
            };

    public static bool IsPostalCode(string asked)
    {
        ArgumentNullException.ThrowIfNull(asked);

        return asked.Length == PostalCodeLength && asked.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Whether <paramref name="asked"/> is a postal code a change takes: seven digits, or empty or none for one not
    /// registered.
    /// </summary>
    public static bool TakesPostalCode(string? asked) => string.IsNullOrEmpty(asked) || IsPostalCode(asked);

    /// <summary>
    /// The postal code <paramref name="asked"/> registers, none for an empty one. It is one
    /// <see cref="TakesPostalCode"/> takes.
    /// </summary>
    public static string? PostalCodeOf(string? asked)
    {
        if (!TakesPostalCode(asked))
        {
            throw new ArgumentOutOfRangeException(nameof(asked), asked, "A postal code is seven digits, or empty.");
        }

        return string.IsNullOrEmpty(asked) ? null : asked;
    }

    /// <summary>
    /// Whether <paramref name="asked"/> is a prefecture a change takes: from <see cref="FirstPrefecture"/> to
    /// <see cref="LastPrefecture"/>, or none for one not registered.
    /// </summary>
    public static bool TakesPrefecture(int? asked) => asked is null or (>= FirstPrefecture and <= LastPrefecture);

    /// <summary>
    /// The row once the postal code and the prefecture are set to <paramref name="postalCode"/> and
    /// <paramref name="prefecture"/> at <paramref name="at"/>; none when neither moves. An empty postal code is one not
    /// registered.
    /// </summary>
    public DataBroadcastSettings? Set(string? postalCode, int? prefecture, DateTime at)
    {
        string? registered = PostalCodeOf(postalCode);

        if (!TakesPrefecture(prefecture))
        {
            throw new ArgumentOutOfRangeException(nameof(prefecture), prefecture, "A prefecture is set from 0 to 49, or to none.");
        }

        return registered == PostalCode && prefecture == Prefecture
            ? null
            : DataBroadcastSettings.Rehydrate(DataBroadcastSettings.TheOnlyRow, registered, prefecture, at);
    }
}
