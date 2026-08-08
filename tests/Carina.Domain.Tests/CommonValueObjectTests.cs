namespace Carina.Domain.Tests;

public sealed class CommonValueObjectTests
{
    [Fact]
    public void SameTypeAndValueAreEqual()
    {
        Assert.Equal(new NetworkId(32736), new NetworkId(32736));
        Assert.Equal(new NetworkId(32736).GetHashCode(), new NetworkId(32736).GetHashCode());
    }

    [Fact]
    public void SameTypeWithDifferentValuesAreNotEqual()
    {
        Assert.NotEqual(new NetworkId(32736), new NetworkId(32737));
    }

    [Fact]
    public void DifferentTypesSharingAValueAreNotEqual()
    {
        CommonValueObject<int> networkId = new NetworkId(1024);
        CommonValueObject<int> serviceId = new ServiceId(1024);

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
