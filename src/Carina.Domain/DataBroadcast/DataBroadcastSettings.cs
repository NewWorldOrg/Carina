using Carina.Domain.Base;

namespace Carina.Domain.DataBroadcast;

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

    public static bool TakesPostalCode(string? asked) => string.IsNullOrEmpty(asked) || IsPostalCode(asked);

    public static string? PostalCodeOf(string? asked)
    {
        if (!TakesPostalCode(asked))
        {
            throw new ArgumentOutOfRangeException(nameof(asked), asked, "A postal code is seven digits, or empty.");
        }

        return string.IsNullOrEmpty(asked) ? null : asked;
    }

    public static bool TakesPrefecture(int? asked) => asked is null or (>= FirstPrefecture and <= LastPrefecture);

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
