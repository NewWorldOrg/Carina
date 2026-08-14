using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tests.Building;

namespace Carina.Broadcast.Tests.Descriptors;

public sealed class SiDescriptorTests
{
    private const int SomeServiceId = 50101;
    private const int AnotherServiceId = 50102;

    [Fact]
    public void AServiceDescriptionCarriesItsKindItsProviderAndItsName()
    {
        var descriptor = Only(SiDescriptorWriter.Service(
            (int)ServiceKind.Television,
            new AribTextWriter().Kanji("試験").ToArray(),
            new AribTextWriter().Kanji("試験").KatakanaBySingleShift("テレビ").ToArray()));

        Assert.True(ServiceDescription.TryRead(descriptor, out var description));
        Assert.Equal(ServiceKind.Television, description.Kind);
        Assert.Equal("試験", description.ProviderName);
        Assert.Equal("試験テレビ", description.Name);
    }

    [Fact]
    public void AServiceDescriptionWithNeitherProviderNorNameIsStillWellFormed()
    {
        var descriptor = Only(SiDescriptorWriter.Service((int)ServiceKind.Data, [], []));

        Assert.True(ServiceDescription.TryRead(descriptor, out var description));
        Assert.Equal(string.Empty, description.ProviderName);
        Assert.Equal(string.Empty, description.Name);
    }

    [Fact]
    public void ANameLengthReachingPastTheDescriptorIsRefused()
    {
        var descriptor = Only(DescriptorWriter.Of(DescriptorTags.Service, 0x01, 0x00, 0x20, 0x41));

        Assert.False(ServiceDescription.TryRead(descriptor, out var description));
        Assert.Null(description);
    }

    [Fact]
    public void AProviderLengthReachingPastTheDescriptorIsRefused()
    {
        var descriptor = Only(DescriptorWriter.Of(DescriptorTags.Service, 0x01, 0x20, 0x41));

        Assert.False(ServiceDescription.TryRead(descriptor, out _));
    }

    [Fact]
    public void ADescriptorOfAnotherTagIsNotAServiceDescription()
    {
        Assert.False(ServiceDescription.TryRead(Only(DescriptorWriter.Of(0x49, 0x01, 0x00, 0x00)), out _));
    }

    [Fact]
    public void AServiceListNamesEveryServiceOnTheStreamWithItsKind()
    {
        var descriptor = Only(SiDescriptorWriter.ServiceList(
            (SomeServiceId, (int)ServiceKind.Television),
            (AnotherServiceId, (int)ServiceKind.Data)));

        Assert.True(ServiceListDescriptor.TryRead(descriptor, out var services));
        Assert.Equal<int>(
            [SomeServiceId, AnotherServiceId],
            services.Select(service => service.ServiceId).ToArray());
        Assert.Equal(ServiceKind.Television, services[0].Kind);
        Assert.Equal(ServiceKind.Data, services[1].Kind);
    }

    [Fact]
    public void AServiceListThatDoesNotDivideIntoEntriesIsRefused()
    {
        var descriptor = Only(DescriptorWriter.Of(DescriptorTags.ServiceList, 0x00, 0x01, 0x01, 0x02));

        Assert.False(ServiceListDescriptor.TryRead(descriptor, out var services));
        Assert.Null(services);
    }

    [Fact]
    public void ANetworkNameIsBroadcastTextLikeAnyOther()
    {
        var descriptor = Only(SiDescriptorWriter.NetworkName(new AribTextWriter().Kanji("試験").ToArray()));

        Assert.True(NetworkNameDescriptor.TryRead(descriptor, out var name));
        Assert.Equal("試験", name);
    }

    [Fact]
    public void TheStreamInformationCarriesTheRemoteControlKeyTheGuideWillWant()
    {
        var descriptor = Only(SiDescriptorWriter.TransportStreamInformation(
            9,
            new AribTextWriter().Kanji("試験").ToArray(),
            (0xFF, [SomeServiceId, AnotherServiceId])));

        Assert.True(TransportStreamInformation.TryRead(descriptor, out var information));
        Assert.Equal(9, information.RemoteControlKeyId);
        Assert.Equal("試験", information.Name);
    }

    [Fact]
    public void AStreamNameLengthReachingPastTheDescriptorIsRefused()
    {
        var descriptor = Only(DescriptorWriter.Of(DescriptorTags.TransportStreamInformation, 0x09, 0xFC, 0x41));

        Assert.False(TransportStreamInformation.TryRead(descriptor, out _));
    }

    [Fact]
    public void PartialReceptionNamesTheServicesThatAreCarriedNarrowly()
    {
        var descriptor = Only(SiDescriptorWriter.PartialReception(SomeServiceId, AnotherServiceId));

        Assert.True(PartialReceptionDescriptor.TryRead(descriptor, out var services));
        Assert.Equal<int>([SomeServiceId, AnotherServiceId], services.ToArray());
    }

    [Fact]
    public void APartialReceptionListWithAnOddByteIsRefused()
    {
        var descriptor = Only(DescriptorWriter.Of(DescriptorTags.PartialReception, 0x00, 0x01, 0x02));

        Assert.False(PartialReceptionDescriptor.TryRead(descriptor, out _));
    }

    private static Descriptor Only(byte[] descriptor)
    {
        Assert.True(DescriptorLoop.TryRead(descriptor, out var descriptors));

        return Assert.Single(descriptors);
    }
}
