using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeNoteTests
{
    [Fact(DisplayName = "what an encode failure keeps names no path on this machine, as the live side already did")]
    public void WhatAnEncodeFailureKeepsNamesNoPathOnThisMachine()
    {
        const string said = "[out#0 @ 0x1] Error opening output /srv/encoded/k-1.mp4: No space left on device";

        Assert.DoesNotContain('/', EncodeNote.Of(said));
        Assert.Contains("No space left on device", EncodeNote.Of(said), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLiveSideAndTheEncodeSideHideAPathTheSameWay()
    {
        const string said = "could not open /dev/dri/renderD128";

        Assert.Equal(TranscoderNote.Of(said), EncodeNote.Of(said));
        Assert.Equal(ProgrammeNote.Of(said, ProgrammeNote.Longest), EncodeNote.Of(said));
    }
}
