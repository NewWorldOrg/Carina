using System.Reflection;

using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveRateControlTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3000)]
    public void ACapOfNothingASecondIsRefused(int kilobitsPerSecond)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitrateCap(kilobitsPerSecond));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3000)]
    public void ACapIsTheRateItNames(int kilobitsPerSecond)
    {
        Assert.Equal(kilobitsPerSecond, new BitrateCap(kilobitsPerSecond).KilobitsPerSecond);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(52)]
    public void AQuantiserOutsideWhatTheCodecQuantisesIsRefused(int quantiser)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantQuantiser(quantiser));
    }

    [Theory]
    [InlineData(ConstantQuantiser.Finest)]
    [InlineData(24)]
    [InlineData(ConstantQuantiser.Coarsest)]
    public void AQuantiserOnTheEdgeOfWhatTheCodecQuantisesIsTaken(int quantiser)
    {
        Assert.Equal(quantiser, new ConstantQuantiser(quantiser).Quantiser);
    }

    [Theory(DisplayName = "an AV1 quality index outside what the card quantises between is refused")]
    [InlineData(-1)]
    [InlineData(256)]
    public void AnAv1QualityIndexOutsideWhatTheCardQuantisesBetweenIsRefused(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QualityIndex(index));
    }

    [Theory(DisplayName = "an AV1 quality index on the edge of what the card quantises between is taken")]
    [InlineData(QualityIndex.Finest)]
    [InlineData(80)]
    [InlineData(QualityIndex.Coarsest)]
    public void AnAv1QualityIndexOnTheEdgeIsTaken(int index)
    {
        Assert.Equal(index, new QualityIndex(index).Index);
    }

    [Fact(DisplayName = "the card is handed a quantiser for H.264 and H.265 and a quality index for AV1")]
    public void TheCardIsHandedAQuantiserForH264AndH265AndAQualityIndexForAv1()
    {
        CardRateControl handed = new(new ConstantQuantiser(24), new ConstantQuantiser(26), new QualityIndex(80));

        Assert.Equal(new ConstantQuantiser(24), handed.H264);
        Assert.Equal(new ConstantQuantiser(26), handed.H265);
        Assert.Equal(new QualityIndex(80), handed.Av1);
        Assert.Throws<ArgumentNullException>(() => new CardRateControl(null!, new ConstantQuantiser(24), new QualityIndex(80)));
        Assert.Throws<ArgumentNullException>(() => new CardRateControl(new ConstantQuantiser(24), null!, new QualityIndex(80)));
        Assert.Throws<ArgumentNullException>(() => new CardRateControl(new ConstantQuantiser(24), new ConstantQuantiser(24), null!));
    }

    [Fact]
    public void NothingTheCardIsHandedCanBeChangedOnceItIsMade()
    {
        Assert.All(
            typeof(CardRateControl).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Concat(typeof(QualityIndex).GetProperties(BindingFlags.Public | BindingFlags.Instance)),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void TwoCapsOfTheSameRateAreTheSameCap()
    {
        Assert.Equal(new BitrateCap(3000), new BitrateCap(3000));
        Assert.NotEqual(new BitrateCap(3000), new BitrateCap(4500));
    }

    [Fact]
    public void AQuantiserCarriesAQuantiserAndNothingElse()
    {
        Assert.Equal(
            [nameof(ConstantQuantiser.Quantiser)],
            typeof(ConstantQuantiser)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name));
    }

    [Fact]
    public void NothingInTheDomainNamedForVaapiHoldsABitrate()
    {
        Assert.Empty(MembersPairingVaapiWithABitrate(typeof(BitrateCap).Assembly));
    }

    [Fact]
    public void TheRuleCatchesAMemberNamedForVaapiTypedAsABitrate()
    {
        Assert.Contains(
            $"{typeof(VaapiRateControlFixtures).FullName}.{nameof(VaapiRateControlFixtures.VaapiRateControl)}",
            MembersPairingVaapiWithABitrate(typeof(VaapiRateControlFixtures).Assembly),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheRuleDoesNotSeeARateWrittenOutAsAPlainNumber()
    {
        Assert.DoesNotContain(
            $"{typeof(VaapiRateControlFixtures).FullName}.{nameof(VaapiRateControlFixtures.VaapiKilobitsPerSecond)}",
            MembersPairingVaapiWithABitrate(typeof(VaapiRateControlFixtures).Assembly),
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> MembersPairingVaapiWithABitrate(Assembly assembly)
        =>
        [
            .. from type in assembly.GetTypes()
               where type.IsPublic
               from member in Named(type)
               where member.Name.Contains("vaapi", StringComparison.OrdinalIgnoreCase)
                     && member.Type == typeof(BitrateCap)
               select $"{type.FullName}.{member.Name}",
        ];

    private static IEnumerable<(string Name, Type Type)> Named(Type type)
    {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return (property.Name, property.PropertyType);
        }

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return ($"{method.Name}.{parameter.Name}", parameter.ParameterType);
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors())
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                yield return ($"{type.Name}.{parameter.Name}", parameter.ParameterType);
            }
        }
    }
}

public sealed class VaapiRateControlFixtures
{
    public BitrateCap VaapiRateControl => new(3000);

    public int VaapiKilobitsPerSecond => 3000;
}
