using Carina.Broadcast.Descriptors;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Descriptors;

public sealed class DescriptorLoopTests
{
    [Fact]
    public void EachDescriptorComesBackWithItsTagAndItsOwnBytes()
    {
        var loop = DescriptorWriter.Loop(
            DescriptorWriter.Of(0x40, 0x01, 0x02),
            DescriptorWriter.Of(0x41, 0x03));

        Assert.True(DescriptorLoop.TryRead(loop, out var descriptors));
        Assert.Equal<int>([0x40, 0x41], descriptors.Select(descriptor => descriptor.Tag).ToArray());
        Assert.Equal<byte[]>([0x01, 0x02], descriptors[0].Payload.ToArray());
        Assert.Equal<byte[]>([0x03], descriptors[1].Payload.ToArray());
    }

    [Fact]
    public void ATagNoOneKnowsIsCarriedRatherThanRefused()
    {
        var loop = DescriptorWriter.Loop(
            DescriptorWriter.Of(0x40, 0x01),
            DescriptorWriter.Of(0x7F, 0xAA, 0xBB, 0xCC));

        Assert.True(DescriptorLoop.TryRead(loop, out var descriptors));
        Assert.Equal(2, descriptors.Count);
        Assert.Equal<byte[]>([0xAA, 0xBB, 0xCC], descriptors.WithTag(0x7F)!.Payload.ToArray());
    }

    [Fact]
    public void ADeclaredLengthThatOverrunsTheLoopRefusesTheWholeLoop()
    {
        var loop = DescriptorWriter.Loop(
            DescriptorWriter.Of(0x40, 0x01),
            DescriptorWriter.Overrunning(0x41, declaredLength: 40, 0x02, 0x03));

        Assert.False(DescriptorLoop.TryRead(loop, out var descriptors));
        Assert.Empty(descriptors);
    }

    [Fact]
    public void ATagWithNoLengthByteBehindItRefusesTheWholeLoop()
    {
        var loop = DescriptorWriter.Loop(DescriptorWriter.Of(0x40, 0x01), [0x41]);

        Assert.False(DescriptorLoop.TryRead(loop, out var descriptors));
        Assert.Empty(descriptors);
    }

    [Fact]
    public void AnEmptyLoopHoldsNoDescriptorsAndIsStillWellFormed()
    {
        Assert.True(DescriptorLoop.TryRead(ReadOnlyMemory<byte>.Empty, out var descriptors));
        Assert.Empty(descriptors);
    }

    [Fact]
    public void ADescriptorWithNoPayloadIsStillADescriptor()
    {
        Assert.True(DescriptorLoop.TryRead(DescriptorWriter.Of(0x40), out var descriptors));
        Assert.Equal(0x40, Assert.Single(descriptors).Tag);
        Assert.True(descriptors[0].Payload.IsEmpty);
    }

    [Fact]
    public void LookingForATagThatIsNotThereFindsNothing()
    {
        Assert.True(DescriptorLoop.TryRead(DescriptorWriter.Of(0x40, 0x01), out var descriptors));
        Assert.Null(descriptors.WithTag(0x48));
    }
}
