using System.Collections.Concurrent;

using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Tests.Thumbnails;

internal sealed record Illustrated(RecordingId Id, ThumbnailState State, ThumbnailFault? Fault);

internal sealed class HeldWorklist : IThumbnailWorklist
{
    private readonly List<ThumbnailSubject> awaiting = [];

    public List<Illustrated> Written { get; } = [];

    public List<RecordingId> AskedAgain { get; } = [];

    public int Reads { get; private set; }

    public int? AskedFor { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public HeldWorklist Holding(params ThumbnailSubject[] subjects)
    {
        awaiting.AddRange(subjects);

        return this;
    }

    public async Task<IReadOnlyList<ThumbnailSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken)
    {
        Reads++;
        AskedFor = atMost;

        if (Gate is { } waiting)
        {
            await waiting.Task.WaitAsync(cancellationToken);
        }

        return [.. awaiting.Take(atMost)];
    }

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

internal sealed class HeldRenderer : IThumbnailRenderer
{
    private readonly Func<ThumbnailRequest, ThumbnailRender> answer;

    public HeldRenderer(Func<ThumbnailRequest, ThumbnailRender>? answer = null)
    {
        this.answer = answer ?? (_ => ThumbnailRender.Drawn());
    }

    public ConcurrentQueue<ThumbnailRequest> Asked { get; } = new();

    public Task<ThumbnailRender> RenderAsync(ThumbnailRequest request, CancellationToken cancellationToken)
    {
        Asked.Enqueue(request);

        return Task.FromResult(answer(request));
    }
}

internal sealed class ThrowingRenderer : IThumbnailRenderer
{
    public Task<ThumbnailRender> RenderAsync(ThumbnailRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("the renderer fell over");
}
