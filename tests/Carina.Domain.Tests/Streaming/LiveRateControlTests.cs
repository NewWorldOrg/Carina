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
    public void NothingNamedForVaapiCanHoldABitrate()
    {
        string[] pairings =
        [
            .. from type in typeof(BitrateCap).Assembly.GetTypes()
               where type.IsPublic
               from member in Named(type)
               where member.Name.Contains("vaapi", StringComparison.OrdinalIgnoreCase)
                     && member.Type == typeof(BitrateCap)
               select $"{type.FullName}.{member.Name}",
        ];

        Assert.Empty(pairings);
    }

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
