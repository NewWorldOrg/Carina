using Carina.Domain.Encodings;

namespace Carina.Api.Services;

public static class EncodeSaying
{
    public static string Retired(EncodeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return $"Profile {profile.Id.Wire} was retired at {Moment(profile.RetiredAt)}; a retired definition stands as it was so that what was encoded with it still reads.";
    }

    public static string Retired(EncodeDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return $"Destination {destination.Id.Wire} was retired at {Moment(destination.RetiredAt)}; a retired definition stands as it was so that what was encoded with it still reads.";
    }

    public static string NotOffered(EncodeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return $"Profile {profile.Id.Wire} was retired at {Moment(profile.RetiredAt)} and nothing is encoded with it again.";
    }

    public static string NotOffered(EncodeDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return $"Destination {destination.Id.Wire} was retired at {Moment(destination.RetiredAt)} and takes nothing new.";
    }

    public static string Underway(string what, string wire, EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return $"{what} {wire} is what job {job.Id.Wire} is {Standing(job)}, and it stands still until that job has ended or been called off.";
    }

    private static string Standing(EncodeJob job)
        => job.Status is EncodeJobStatus.Running ? "running with" : "waiting to run with";

    private static string Moment(DateTime? at) => at?.ToString("O") ?? "an hour the ledger did not write down";
}
