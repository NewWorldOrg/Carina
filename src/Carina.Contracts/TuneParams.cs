using System.Text.Json.Serialization;

namespace Carina.Contracts;

[JsonConverter(typeof(TuneSystemConverter))]
public enum TuneSystem
{
    Unspecified = 0,

    IsdbT = 1,

    IsdbSBs = 2,

    IsdbSCs110 = 3,
}

public sealed record IsdbTParams(int PhysicalChannel);

public sealed record IsdbSBsParams(int BsChannel, int Tsid);

public sealed record IsdbSCs110Params(int CsChannel);

public sealed record TuneParams
{
    public TuneSystem System { get; init; }

    public IsdbTParams? IsdbT { get; init; }

    public IsdbSBsParams? IsdbSBs { get; init; }

    public IsdbSCs110Params? IsdbSCs110 { get; init; }

    public static TuneParams Terrestrial(int physicalChannel) =>
        new() { System = TuneSystem.IsdbT, IsdbT = new IsdbTParams(physicalChannel) };

    public static TuneParams Bs(int bsChannel, int tsid) =>
        new() { System = TuneSystem.IsdbSBs, IsdbSBs = new IsdbSBsParams(bsChannel, tsid) };

    public static TuneParams Cs110(int csChannel) =>
        new() { System = TuneSystem.IsdbSCs110, IsdbSCs110 = new IsdbSCs110Params(csChannel) };

    [JsonIgnore]
    public TunerKind Kind =>
        System switch
        {
            TuneSystem.IsdbT => TunerKind.Terrestrial,
            TuneSystem.IsdbSBs or TuneSystem.IsdbSCs110 => TunerKind.Satellite,
            _ => TunerKind.Unspecified,
        };

    public TuningRequest ToLegacyRequest() =>
        System switch
        {
            TuneSystem.IsdbT => new TuningRequest(
                TunerKind.Terrestrial,
                IsdbT?.PhysicalChannel ?? 0
            ),
            TuneSystem.IsdbSBs => new TuningRequest(
                TunerKind.Unspecified,
                IsdbSBs?.BsChannel ?? 0
            ),
            TuneSystem.IsdbSCs110 => new TuningRequest(
                TunerKind.Unspecified,
                IsdbSCs110?.CsChannel ?? 0
            ),
            _ => new TuningRequest(TunerKind.Unspecified, 0),
        };

    public IReadOnlyList<string> Validate()
    {
        if (System is TuneSystem.Unspecified)
        {
            return ["system: missing, or a value this driver does not know."];
        }

        var problems = new List<string>();

        problems.AddRange(TerrestrialProblems());
        problems.AddRange(BsProblems());
        problems.AddRange(Cs110Problems());

        return problems;
    }

    private IReadOnlyList<string> TerrestrialProblems()
    {
        if (System is not TuneSystem.IsdbT)
        {
            return IsdbT is null ? [] : [Unwanted(nameof(IsdbT))];
        }

        if (IsdbT is null)
        {
            return [Missing(nameof(IsdbT))];
        }

        return BroadcastStandards.IsTerrestrialChannel(IsdbT.PhysicalChannel)
            ? []
            :
            [
                $"isdbT.physicalChannel: expected {BroadcastStandards.TerrestrialFirstChannel} to {BroadcastStandards.TerrestrialLastChannel}, got {IsdbT.PhysicalChannel}.",
            ];
    }

    private IReadOnlyList<string> BsProblems()
    {
        if (System is not TuneSystem.IsdbSBs)
        {
            return IsdbSBs is null ? [] : [Unwanted(nameof(IsdbSBs))];
        }

        if (IsdbSBs is null)
        {
            return [Missing(nameof(IsdbSBs))];
        }

        var problems = new List<string>();

        if (!BroadcastStandards.IsBsChannel(IsdbSBs.BsChannel))
        {
            problems.Add(
                $"isdbSBs.bsChannel: expected an odd {BroadcastStandards.BsFirstChannel} to {BroadcastStandards.BsLastChannel} other than {string.Join(" and ", BroadcastStandards.BsChannelsWithoutDemodulation)}, got {IsdbSBs.BsChannel}."
            );
        }

        if (!BroadcastStandards.IsTransportStreamId(IsdbSBs.Tsid))
        {
            problems.Add(
                $"isdbSBs.tsid: expected {BroadcastStandards.MinTransportStreamId} to {BroadcastStandards.MaxTransportStreamId}, got {IsdbSBs.Tsid}."
            );
        }

        return problems;
    }

    private IReadOnlyList<string> Cs110Problems()
    {
        if (System is not TuneSystem.IsdbSCs110)
        {
            return IsdbSCs110 is null ? [] : [Unwanted(nameof(IsdbSCs110))];
        }

        if (IsdbSCs110 is null)
        {
            return [Missing(nameof(IsdbSCs110))];
        }

        return BroadcastStandards.IsCs110Channel(IsdbSCs110.CsChannel)
            ? []
            :
            [
                $"isdbSCs110.csChannel: expected an even {BroadcastStandards.Cs110FirstChannel} to {BroadcastStandards.Cs110LastChannel}, got {IsdbSCs110.CsChannel}.",
            ];
    }

    private string Missing(string arm) =>
        $"{Camel(arm)}: missing for a tune on {TuneSystemConverter.WireName(System)}.";

    private string Unwanted(string arm) =>
        $"{Camel(arm)}: only the parameters of {TuneSystemConverter.WireName(System)} may be filled.";

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
