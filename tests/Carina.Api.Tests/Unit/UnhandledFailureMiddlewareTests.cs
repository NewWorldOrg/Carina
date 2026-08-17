using System.Text.Json;

using Carina.Api.Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Unit;

public sealed class UnhandledFailureMiddlewareTests
{
    private readonly RecordingLogger logger = new();

    [Fact]
    public async Task ARequestThatFailedIsAnsweredWithTheUsualEnvelope()
    {
        var context = Context();

        await Middleware(_ => throw new InvalidOperationException("no answer of its own"))
            .InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        var answered = await ReadAsync(context);

        Assert.False(answered.GetProperty("status").GetBoolean());
        Assert.Equal(UnhandledFailureMiddleware.Message, answered.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, answered.GetProperty("data").ValueKind);
        Assert.Contains(LogLevel.Error, logger.Levels);
    }

    [Fact]
    public async Task AFailureAfterTheAnswerBeganIsLeftToEndTheConnection()
    {
        var context = Context();
        var response = (StartableResponse)context.Features.Get<IHttpResponseFeature>()!;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Middleware(_ =>
                {
                    response.HasStarted = true;

                    throw new InvalidOperationException("half an answer is already out");
                })
                .InvokeAsync(context));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task ARequestTheCallerAbandonedIsNotReportedAsAFailure()
    {
        var abandoned = new CancellationTokenSource();
        var context = Context();
        context.RequestAborted = abandoned.Token;

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => Middleware(_ =>
                {
                    abandoned.Cancel();

                    return Task.FromCanceled(abandoned.Token);
                })
                .InvokeAsync(context));

        Assert.DoesNotContain(LogLevel.Error, logger.Levels);
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartableResponse());
        context.Request.Method = "POST";
        context.Request.Path = "/api/anything";
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static async Task<JsonElement> ReadAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;

        return (await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body))!;
    }

    private UnhandledFailureMiddleware Middleware(RequestDelegate next) => new(next, logger);

    private sealed class StartableResponse : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted { get; set; }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class RecordingLogger : ILogger<UnhandledFailureMiddleware>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }
}
