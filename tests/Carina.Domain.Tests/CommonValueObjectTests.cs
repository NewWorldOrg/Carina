namespace Carina.Domain.Tests;

public sealed class CommonValueObjectTests
{
    [Fact]
    public void SameTypeAndValueAreEqual()
    {
        Assert.Equal(new NetworkId(40001), new NetworkId(40001));
        Assert.Equal(new NetworkId(40001).GetHashCode(), new NetworkId(40001).GetHashCode());
    }

    [Fact]
    public void SameTypeWithDifferentValuesAreNotEqual()
    {
        Assert.NotEqual(new NetworkId(40001), new NetworkId(40002));
    }

    [Fact]
    public void DifferentTypesSharingAValueAreNotEqual()
    {
        CommonValueObject<int> networkId = new NetworkId(50001);
        CommonValueObject<int> serviceId = new ServiceId(50001);

        Assert.NotEqual(networkId, serviceId);
    }

    [Fact]
    public void NullValueIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Label(null!));
    }

    private sealed class NetworkId(int value) : CommonValueObject<int>(value);

    private sealed class ServiceId(int value) : CommonValueObject<int>(value);

    private sealed class Label(string value) : CommonValueObject<string>(value);
}
