using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveChannelQueryTests
{
    [Fact]
    public void AskingForNothingInParticularListsByRemoteControlKeyFiftyAtATimeWithNoExtras()
    {
        LiveChannelQuery query = LiveChannelQuery.For()!;

        Assert.Equal(LiveChannelSort.RemoteControlKey, query.Sort);
        Assert.False(query.Descending);
        Assert.Empty(query.Fields);
        Assert.Equal(1, query.Page);
        Assert.Equal(LiveChannelQuery.DefaultPerPage, query.PerPage);
    }

    [Theory]
    [InlineData(null, LiveChannelQuery.DefaultPerPage)]
    [InlineData(0, LiveChannelQuery.DefaultPerPage)]
    [InlineData(-5, LiveChannelQuery.DefaultPerPage)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    [InlineData(201, LiveChannelQuery.MostPerPage)]
    [InlineData(int.MaxValue, LiveChannelQuery.MostPerPage)]
    public void APageSizeIsClampedToTheCeilingRatherThanRefused(int? asked, int used)
    {
        Assert.Equal(used, LiveChannelQuery.For(perPage: asked)!.PerPage);
    }

    [Fact]
    public void TheCeilingIsTheOneEveryListInThisApplicationHas()
    {
        Assert.Equal(200, LiveChannelQuery.MostPerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageBelowTheFirstIsRefused(int page)
    {
        Assert.Null(LiveChannelQuery.For(page: page));
    }

    [Fact]
    public void ASortOffTheListIsRefused()
    {
        Assert.Null(LiveChannelQuery.For(sort: (LiveChannelSort)99));
    }

    [Fact]
    public void AFieldOffTheListIsRefused()
    {
        Assert.Null(LiveChannelQuery.For(fields: [LiveChannelField.Sessions, (LiveChannelField)99]));
    }

    [Fact]
    public void TheFieldsAskedForAreKeptOnceEach()
    {
        LiveChannelQuery query = LiveChannelQuery.For(fields: [LiveChannelField.Tuning, LiveChannelField.Sessions, LiveChannelField.Tuning])!;

        Assert.Equal([LiveChannelField.Tuning, LiveChannelField.Sessions], query.Fields);
        Assert.True(query.Asks(LiveChannelField.Tuning));
        Assert.True(query.Asks(LiveChannelField.Sessions));
    }

    [Fact]
    public void AFieldNotAskedForIsNotAnswered()
    {
        Assert.False(LiveChannelQuery.For()!.Asks(LiveChannelField.Sessions));
    }

    [Theory]
    [InlineData(LiveChannelSort.RemoteControlKey)]
    [InlineData(LiveChannelSort.Name)]
    [InlineData(LiveChannelSort.Viewers)]
    public void EverySortOnTheListIsTaken(LiveChannelSort sort)
    {
        Assert.Equal(sort, LiveChannelQuery.For(sort: sort, descending: true)!.Sort);
        Assert.True(LiveChannelQuery.For(sort: sort, descending: true)!.Descending);
    }
}
