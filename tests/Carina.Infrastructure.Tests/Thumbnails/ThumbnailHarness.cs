using System.Collections.Concurrent;

using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Thumbnails;

internal sealed record Illustrated(RecordingId Id, ThumbnailState State, ThumbnailFault? Fault);

internal sealed class HeldWorklist : IThumbnailWorklist
{
    private readonly List<ThumbnailSubject> awaiting = [];

    public List<Illustrated> Written { get; } = [];

    public List<RecordingId> AskedAgain { get; } = [];

    public int Reads { get; private set; }

    public int? AskedFor { get; private set; }

    public IReadOnlyList<OutputRoot> AskedWithin { get; private set; } = [];

    public TaskCompletionSource? Gate { get; set; }

    public HeldWorklist Holding(params ThumbnailSubject[] subjects)
    {
        awaiting.AddRange(subjects);

        return this;
    }

    public async Task<IReadOnlyList<ThumbnailSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
    {
        Reads++;
        AskedFor = atMost;
        AskedWithin = withinReach;

        if (Gate is { } waiting)
        {
            await waiting.Task.WaitAsync(cancellationToken);
        }

        return [.. awaiting.Where(subject => withinReach.Contains(subject.Root)).Take(atMost)];
    }

    public Task<int> WaitingOutOfReachAsync(
        IReadOnlyList<OutputRoot> withinReach,
        CancellationToken cancellationToken)
        => Task.FromResult(awaiting.Count(subject => !withinReach.Contains(subject.Root)));

    public Task IllustrateAsync(
        RecordingId id,
        ThumbnailState state,
        ThumbnailFault? fault,
        CancellationToken cancellationToken)
    {
        Written.Add(new Illustrated(id, state, fault));

        return Task.CompletedTask;
    }

    public Task<ThumbnailSubject?> AskAgainAsync(RecordingId id, CancellationToken cancellationToken)
    {
        AskedAgain.Add(id);

        return Task.FromResult(awaiting.FirstOrDefault(subject => subject.Id.Equals(id)));
    }
}

internal sealed class HeldRenderer(
    Func<ThumbnailRequest, ThumbnailRender>? answer = null,
    Func<ThumbnailFrameRequest, ThumbnailRender>? framed = null) : IThumbnailRenderer
{
    private readonly Func<ThumbnailRequest, ThumbnailRender> answer = answer ?? (_ => ThumbnailRender.Drawn());

    private readonly Func<ThumbnailFrameRequest, ThumbnailRender> framed =
        framed ?? (_ => ThumbnailRender.Drawn([0xff, 0xd8]));

    public ConcurrentQueue<ThumbnailRequest> Asked { get; } = new();

    public ConcurrentQueue<ThumbnailFrameRequest> AskedForAFrame { get; } = new();

    public Task<ThumbnailRender> RenderAsync(ThumbnailRequest request, CancellationToken cancellationToken)
    {
        Asked.Enqueue(request);

        return Task.FromResult(answer(request));
    }

    public Task<ThumbnailRender> FrameAsync(ThumbnailFrameRequest request, CancellationToken cancellationToken)
    {
        AskedForAFrame.Enqueue(request);

        return Task.FromResult(framed(request));
    }
}

internal sealed class ThrowingRenderer : IThumbnailRenderer
{
    public Task<ThumbnailRender> RenderAsync(ThumbnailRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("the renderer fell over");

    public Task<ThumbnailRender> FrameAsync(ThumbnailFrameRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("the renderer fell over");
}

internal sealed class HeardOf<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> said = new();

    public IReadOnlyCollection<string> Said => said;

    public IEnumerable<string> Warnings => said.Where(line => line.StartsWith("Warning ", StringComparison.Ordinal));

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
    {
        ArgumentNullException.ThrowIfNull(formatter);

        said.Enqueue($"{logLevel} {formatter(state, exception)}");
    }
}
