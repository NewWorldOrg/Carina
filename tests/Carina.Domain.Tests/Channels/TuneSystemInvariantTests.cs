using System.Reflection;
using System.Runtime.CompilerServices;

using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class TuneSystemInvariantTests
{
    private static readonly IReadOnlyDictionary<string, Func<TuningParameters>> Constructions =
        new Dictionary<string, Func<TuningParameters>>(StringComparer.Ordinal)
        {
            ["TuningParameters.Terrestrial"] = () => TuningParameters.Terrestrial(13),
            ["TuningParameters.Bs"] = () => TuningParameters.Bs(1, new TransportStreamId(0x4010)),
            ["TuningParameters.Cs110"] = () => TuningParameters.Cs110(2),
            ["SatelliteTransportStream.ToTuningParameters"] = () =>
                SatelliteTransportStream.Rehydrate(1, 0, new TransportStreamId(0x4010))
                    .ToTuningParameters(),
        };

    private static IReadOnlyList<string> ConstructionPaths =>
        [
            .. typeof(TuningParameters).Assembly
                .GetExportedTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(method => method.ReturnType == typeof(TuningParameters))
                .Where(method => !method.IsSpecialName)
                .Where(method => method.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
                .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    [Fact]
    public void NoTuningTheDomainCanExpressCarriesTheUnspecifiedSystem()
    {
        Assert.NotEmpty(Constructions);
        Assert.All(
            Constructions,
            construction => Assert.NotEqual(
                TuneSystem.Unspecified,
                construction.Value().System));
    }

    [Fact]
    public void EveryWayOfReachingATuningIsWeighedByThatRule()
    {
        Assert.Empty(typeof(TuningParameters).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.NotEmpty(ConstructionPaths);
        Assert.All(
            ConstructionPaths,
            path => Assert.True(
                Constructions.ContainsKey(path),
                $"{path} reaches a tuning this rule does not weigh, so nothing would notice if it carried the unspecified system."));
        Assert.All(Constructions.Keys, key => Assert.Contains(key, ConstructionPaths));
    }
}
