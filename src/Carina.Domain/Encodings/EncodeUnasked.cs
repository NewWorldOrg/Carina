namespace Carina.Domain.Encodings;

public enum EncodeUnaskedStanding
{
    Settled = 1,

    NothingIsDefined = 2,

    MoreThanOneIsOffered = 3,

    TheProfileIsNotOffered = 4,
}

public sealed record EncodeUnasked
{
    private EncodeUnasked(EncodeDestination? destination, EncodeProfile? profile, EncodeUnaskedStanding standing)
    {
        Destination = destination;
        Profile = profile;
        Standing = standing;
    }

    public EncodeDestination? Destination { get; }

    public EncodeProfile? Profile { get; }

    public EncodeUnaskedStanding Standing { get; }

    public bool IsSettled => Standing is EncodeUnaskedStanding.Settled;

    public static EncodeUnasked Of(
        IReadOnlyList<EncodeDestination> destinations,
        IReadOnlyList<EncodeProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(profiles);

        EncodeDestination[] offered = [.. destinations.Where(destination => !destination.IsRetired)];

        if (offered is not [EncodeDestination only])
        {
            return new EncodeUnasked(
                null,
                null,
                offered.Length is 0 ? EncodeUnaskedStanding.NothingIsDefined : EncodeUnaskedStanding.MoreThanOneIsOffered);
        }

        EncodeProfile? named = profiles
            .FirstOrDefault(profile => profile.Id.Equals(only.DefaultProfileId) && !profile.IsRetired);

        return named is null
            ? new EncodeUnasked(null, null, EncodeUnaskedStanding.TheProfileIsNotOffered)
            : new EncodeUnasked(only, named, EncodeUnaskedStanding.Settled);
    }
}
