using System.Text;

using Carina.Api.Events;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Unit;

public sealed class ProgrammeFeedStreamTests
{
    private static readonly ProgrammeFeedSettings OneAtATime = new()
    {
        ConcurrentReaders = 1,
        StatementTimeout = TimeSpan.FromSeconds(20),
    };

    [Fact]
    public async Task AFeedAskedForWhileEveryPlaceIsTakenIsToldToComeBackLater()
    {
        var readers = new ProgrammeFeedReaders(OneAtATime);
        DefaultHttpContext context = Asking();

        Assert.True(readers.TryTake(out IDisposable? held));

        await ProgrammeFeedStream.Invoke(context, Feed(new UnboundedReads()), readers);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers[HeaderNames.RetryAfter].ToString());
        Assert.Contains(
            "carries 1 bulk programme feed readers at a time and they are all taken",
            Said(context),
            StringComparison.Ordinal);

        held.Dispose();
    }

    [Fact]
    public async Task AFeedThatWasHandedOverGivesItsPlaceBack()
    {
        var readers = new ProgrammeFeedReaders(OneAtATime);

        await ProgrammeFeedStream.Invoke(Asking(), Feed(new UnboundedReads()), readers);

        Assert.Equal(1, readers.Free);
    }

    [Fact]
    public async Task AFeedWhoseReaderWentAwayGivesItsPlaceBack()
    {
        var readers = new ProgrammeFeedReaders(OneAtATime);

        await Assert.ThrowsAsync<OperationCanceledException>(() => ProgrammeFeedStream.Invoke(
            Asking(),
            Feed(new ReadsThatBreakOff(new OperationCanceledException())),
            readers));

        Assert.Equal(1, readers.Free);
    }

    [Fact]
    public async Task AStatementThatRanOutOfTimeEndsTheFeedBeforeAnythingIsSent()
    {
        var readers = new ProgrammeFeedReaders(OneAtATime);
        DefaultHttpContext context = Asking("2:40");

        await ProgrammeFeedStream.Invoke(
            context,
            Feed(new ReadsThatBreakOff(new ReadTookTooLongException(TimeSpan.FromSeconds(20)))),
            readers);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("20", context.Response.Headers[HeaderNames.RetryAfter].ToString());
        Assert.Equal("2:40", context.Response.Headers[ProgrammeFeedStream.CursorHeader].ToString());
        Assert.Contains("ask again from the cursor", Said(context), StringComparison.Ordinal);
        Assert.Equal(1, readers.Free);
    }

    [Fact]
    public async Task AStatementThatRanOutOfTimeWithNoCursorAskedForNamesNoneToResumeFrom()
    {
        DefaultHttpContext context = Asking();

        await ProgrammeFeedStream.Invoke(
            context,
            Feed(new ReadsThatBreakOff(new ReadTookTooLongException(TimeSpan.FromSeconds(20)))),
            new ProgrammeFeedReaders(OneAtATime));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey(ProgrammeFeedStream.CursorHeader));
        Assert.Contains("ask again from the beginning", Said(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReadIsGivenTheTimeOneStatementIsAllowed()
    {
        var counted = new UnboundedReads();

        await ProgrammeFeedStream.Invoke(Asking(), Feed(counted), new ProgrammeFeedReaders(OneAtATime));

        Assert.Equal(TimeSpan.FromSeconds(20), counted.Patience);
    }

    private static ProgrammeFeedService Feed(IBoundedRead reads)
        => new(new HeldProgrammes(), new HeldEpochs(), reads, OneAtATime, TimeProvider.System);

    private static DefaultHttpContext Asking(string? cursor = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (cursor is not null)
        {
            context.Request.QueryString = new QueryString($"?cursor={cursor}");
        }

        return context;
    }

    private static string Said(HttpContext context)
    {
        context.Response.Body.Position = 0;

        using var reading = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);

        return reading.ReadToEnd();
    }

    private sealed class ReadsThatBreakOff(Exception refusal) : IBoundedRead
    {
        public Task<T> NoLongerThanAsync<T>(
            TimeSpan patience,
            Func<CancellationToken, Task<T>> read,
            CancellationToken cancellationToken)
            => Task.FromException<T>(refusal);
    }
}
