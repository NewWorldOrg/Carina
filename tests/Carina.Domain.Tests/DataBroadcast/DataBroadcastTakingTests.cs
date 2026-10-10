using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastTakingTests
{
    [Fact(DisplayName = "BR-BS-001: a record taken holds at least one module, and one with none is missing instead")]
    public void ARecordTakenHoldsAtLeastOneModule()
    {
        DataBroadcastRecord empty = new(0, Carousels.Entry, [new RecordedCarousel(Carousels.Entry, 1, [])], [], false);
        DataBroadcastRecord held = new(0, Carousels.Entry, [new RecordedCarousel(Carousels.Entry, 1, [Carousels.Version(Carousels.Entry, 0, 1, 0)])], [], false);

        Assert.Throws<ArgumentException>(() => DataBroadcastTaking.Taken(empty));
        Assert.Equal((1, (DataBroadcastFault?)null), (DataBroadcastTaking.Taken(held).Modules, DataBroadcastTaking.Taken(held).Fault));
        Assert.Equal((0, (DataBroadcastRecord?)null), (DataBroadcastTaking.Missing().Modules, DataBroadcastTaking.Missing().Record));
    }

    [Fact]
    public void AFailureSaysWhichAndWhy()
    {
        DataBroadcastTaking failed = DataBroadcastTaking.Failed(DataBroadcastFault.TimedOut, "an hour went by");

        Assert.Equal((DataBroadcastFault.TimedOut, "an hour went by", (DataBroadcastRecord?)null), (failed.Fault, failed.Note, failed.Record));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataBroadcastTaking.Failed((DataBroadcastFault)0, string.Empty));
    }
}
