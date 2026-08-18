using Carina.Api.Services;

namespace Carina.Api.Responder.Epg;

public sealed record EpgRebuiltResponder(int Discarded, int Generation)
{
    public static EpgRebuiltResponder Of(EpgRebuilt rebuilt)
    {
        ArgumentNullException.ThrowIfNull(rebuilt);

        return new EpgRebuiltResponder(rebuilt.Discarded, rebuilt.Generation);
    }
}
