using System.IO.Pipelines;

using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldViewing : IOnTheFlyViewing
{
    private readonly Pipe? endless;

    private int letGo;

    public HeldViewing(OnTheFlyStanding standing, byte[] picture, TranscoderExit? exit = null, bool writesOn = false)
    {
        Standing = standing;
        Completion = Task.FromResult(exit ?? TranscoderExit.Finished());

        if (!writesOn)
        {
            Output = new MemoryStream(picture);

            return;
        }

        endless = new Pipe();
        endless.Writer.WriteAsync(picture).AsTask().GetAwaiter().GetResult();
        Output = endless.Reader.AsStream();
    }

    public OnTheFlyStanding Standing { get; }

    public Stream Output { get; }

    public Task<TranscoderExit> Completion { get; }

    public bool WasLetGo => Volatile.Read(ref letGo) is 1;

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref letGo, 1);
        endless?.Writer.Complete();

        if (endless is null)
        {
            Output.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
