using System.Buffers.Binary;
using System.Text;

using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveFragmenterTests
{
    private static readonly byte[] Head = Joined(Box("ftyp", 24), Box("moov", 300));

    [Fact]
    public void TheHeaderIsTheBoxesThatCameBeforeAnyPicture()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Head, Fragment(1_000)));

        Assert.Null(read.Fault);
        Assert.Equal(LiveFragmentKind.Initialisation, read.Fragments[0].Kind);
        Assert.Equal(Head, read.Fragments[0].Bytes.ToArray());
    }

    [Fact]
    public void TheHeaderIsKeptForWhoeverArrivesLate()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        fragmenter.Read(Joined(Head, Fragment(1_000), Fragment(1_000), Fragment(1_000)));

        Assert.Equal(Head, fragmenter.Initialisation?.ToArray());
    }

    [Fact]
    public void NothingIsKeptBeforeAHeaderHasBeenRead()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        fragmenter.Read(Box("ftyp", 24));

        Assert.Null(fragmenter.Initialisation);
    }

    [Fact]
    public void APictureIsAMoofAndTheMdatBehindIt()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        byte[] one = Fragment(1_000);
        byte[] another = Fragment(2_000);

        LiveFragmenting read = fragmenter.Read(Joined(Head, one, another));

        Assert.Equal(
            [one, another],
            read.Fragments.Where(fragment => fragment.Kind is LiveFragmentKind.Media).Select(fragment => fragment.Bytes.ToArray()));
    }

    [Fact]
    public void NoHeaderBoxLandsAmongThePicturesAndNoPictureBoxAmongTheHeader()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Head, Fragment(1_000), Fragment(1_000)));

        byte[] head = read.Fragments.Single(fragment => fragment.Kind is LiveFragmentKind.Initialisation).Bytes.ToArray();
        byte[] media = Joined([.. read.Fragments.Where(fragment => fragment.Kind is LiveFragmentKind.Media).Select(fragment => fragment.Bytes.ToArray())]);

        Assert.Equal(1, Sightings(head, "ftyp"));
        Assert.Equal(1, Sightings(head, "moov"));
        Assert.Equal(0, Sightings(head, "moof"));
        Assert.Equal(0, Sightings(head, "mdat"));
        Assert.Equal(0, Sightings(media, "ftyp"));
        Assert.Equal(0, Sightings(media, "moov"));
        Assert.Equal(2, Sightings(media, "moof"));
        Assert.Equal(2, Sightings(media, "mdat"));
    }

    [Fact]
    public void HowTheBytesWereCutUpOnTheWayInChangesNothing()
    {
        byte[] whole = Joined(Head, Fragment(1_000), Fragment(700), Fragment(1_300));

        List<byte[]> atOnce = Everything(new LiveFragmenter(LiveTrack.Picture), [whole]);
        List<byte[]> oneByteAtATime = Everything(new LiveFragmenter(LiveTrack.Picture), [.. whole.Select(single => new[] { single })]);
        List<byte[]> inSevens = Everything(new LiveFragmenter(LiveTrack.Picture), [.. whole.Chunk(7)]);

        Assert.Equal(atOnce, oneByteAtATime);
        Assert.Equal(atOnce, inSevens);
    }

    [Fact]
    public void SoundIsFragmentedApartFromPicture()
    {
        LiveFragmenter picture = new(LiveTrack.Picture);
        LiveFragmenter sound = new(LiveTrack.Sound);
        byte[] seen = Joined(Box("ftyp", 24), Box("moov", 300), Fragment(4_000));
        byte[] heard = Joined(Box("ftyp", 24), Box("moov", 120), Fragment(400));

        LiveFragmenting watched = picture.Read(seen);
        LiveFragmenting listened = sound.Read(heard);

        Assert.All(watched.Fragments, fragment => Assert.Equal(LiveTrack.Picture, fragment.Track));
        Assert.All(listened.Fragments, fragment => Assert.Equal(LiveTrack.Sound, fragment.Track));
        Assert.NotEqual(picture.Initialisation?.ToArray(), sound.Initialisation?.ToArray());
        Assert.Equal(LiveTrack.Sound, sound.Track);
    }

    [Fact]
    public void AStreamThatStopsInsideABoxHandsBackNothingHalfMade()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        byte[] whole = Joined(Head, Fragment(1_000), Fragment(1_000));

        LiveFragmenting read = fragmenter.Read(whole[..^400]);
        LiveFragmenting ended = fragmenter.Ended();
        List<LiveFragment> media = [.. read.Fragments.Where(fragment => fragment.Kind is LiveFragmentKind.Media)];

        Assert.Single(media);
        Assert.Empty(ended.Fragments);
        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, ended.Fault);
    }

    [Fact]
    public void AMoofWithNoMdatBehindItIsNotHandedOver()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        fragmenter.Read(Joined(Head, Box("moof", 100)));
        LiveFragmenting ended = fragmenter.Ended();

        Assert.Empty(ended.Fragments);
        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, ended.Fault);
    }

    [Fact]
    public void AStreamThatEndsOnABoxBoundaryComplainsAboutNothing()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        fragmenter.Read(Joined(Head, Fragment(1_000)));

        Assert.Null(fragmenter.Ended().Fault);
    }

    [Fact]
    public void WhatTheMuxerWritesBehindTheLastFragmentIsNeitherHandedOnNorComplainedAbout()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Head, Fragment(1_000), Box("mfra", 40)));
        LiveFragmenting ended = fragmenter.Ended();

        Assert.Equal(2, read.Fragments.Count);
        Assert.Empty(ended.Fragments);
        Assert.Null(ended.Fault);
    }

    [Fact]
    public void AStreamThatEndedBeforeItsHeaderIsComplainedAbout()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        fragmenter.Read(Box("ftyp", 24));

        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, fragmenter.Ended().Fault);
    }

    [Fact]
    public void AStreamThatCarriedNothingAtAllIsComplainedAbout()
    {
        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, new LiveFragmenter(LiveTrack.Picture).Ended().Fault);
    }

    [Fact]
    public void ATrailerCutOffPartWayThroughIsStillComplainedAbout()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        fragmenter.Read(Joined(Head, Fragment(1_000), Box("mfra", 40)[..20]));

        Assert.Equal(LiveFragmentFault.StoppedPartWayThrough, fragmenter.Ended().Fault);
    }

    [Fact]
    public void ASizeNoBoxCouldHaveIsRefused()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Head, Saying("moof", 3)));

        Assert.Equal(LiveFragmentFault.ASizeNoBoxCanHave, read.Fault);
    }

    [Fact]
    public void ABoxThatDeclaresNoEndIsRefused()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Head, Saying("mdat", 0)));

        Assert.Equal(LiveFragmentFault.ABoxWithoutAnEnd, read.Fault);
    }

    [Fact]
    public void ASizeWrittenInSixtyFourBitsIsRead()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        byte[] wide = Joined(Box("moof", 100), WideBox("mdat", 900));

        LiveFragmenting read = fragmenter.Read(Joined(Head, wide));

        Assert.Null(read.Fault);
        Assert.Equal(wide, read.Fragments.Single(fragment => fragment.Kind is LiveFragmentKind.Media).Bytes.ToArray());
    }

    [Fact]
    public void ASixtyFourBitSizeSmallerThanItsOwnHeaderIsRefused()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Head, WideSaying("mdat", 12)));

        Assert.Equal(LiveFragmentFault.ASizeNoBoxCanHave, read.Fault);
    }

    [Fact]
    public void ABoxBiggerThanWhatWillBeHeldIsRefusedOnItsHeaderAlone()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture, largestFragment: 64 * 1024);

        LiveFragmenting read = fragmenter.Read(Joined(Head, Box("moof", 100), Saying("mdat", 3_000_000_000)));

        Assert.Equal(LiveFragmentFault.ABoxTooBigToHold, read.Fault);
    }

    [Fact]
    public void ASixtyFourBitSizeBiggerThanWhatWillBeHeldIsRefusedOnItsHeaderAlone()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture, largestFragment: 64 * 1024);

        LiveFragmenting read = fragmenter.Read(Joined(Head, WideSaying("mdat", 8L * 1024 * 1024 * 1024)));

        Assert.Equal(LiveFragmentFault.ABoxTooBigToHold, read.Fault);
    }

    [Fact]
    public void PicturesThatKeepArrivingDoNotPileUp()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture, largestFragment: 64 * 1024);
        fragmenter.Read(Head);

        for (int handed = 0; handed < 500; handed++)
        {
            Assert.Null(fragmenter.Read(Fragment(40_000)).Fault);
        }

        Assert.Null(fragmenter.Ended().Fault);
    }

    [Fact]
    public void WhatIsNotTheContainerItWasAskedForIsRefused()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Box("moov", 300), Fragment(1_000)));

        Assert.Equal(LiveFragmentFault.NotTheContainerItWasAskedFor, read.Fault);
    }

    [Fact]
    public void APictureBeforeAnyHeaderIsRefused()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);

        LiveFragmenting read = fragmenter.Read(Joined(Box("ftyp", 24), Fragment(1_000)));

        Assert.Equal(LiveFragmentFault.MediaBeforeItsHeader, read.Fault);
    }

    [Fact]
    public void OnceItHasBrokenNothingMoreComesOut()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        fragmenter.Read(Joined(Head, Saying("mdat", 0)));

        LiveFragmenting after = fragmenter.Read(Joined(Fragment(1_000), Fragment(1_000)));

        Assert.Empty(after.Fragments);
        Assert.Equal(LiveFragmentFault.ABoxWithoutAnEnd, after.Fault);
        Assert.Equal(LiveFragmentFault.ABoxWithoutAnEnd, fragmenter.Fault);
    }

    [Fact]
    public void AFragmenterThatCouldNotHoldOneBoxHeaderIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveFragmenter(LiveTrack.Picture, largestFragment: 8));
    }

    private static List<byte[]> Everything(LiveFragmenter fragmenter, IReadOnlyList<byte[]> mouthfuls)
    {
        List<byte[]> read = [];

        foreach (byte[] mouthful in mouthfuls)
        {
            read.AddRange(fragmenter.Read(mouthful).Fragments.Select(fragment => fragment.Bytes.ToArray()));
        }

        return read;
    }

    private static int Sightings(byte[] bytes, string kind)
    {
        byte[] looking = Encoding.ASCII.GetBytes(kind);
        int seen = 0;

        for (int at = 0; at + looking.Length <= bytes.Length; at++)
        {
            if (bytes.AsSpan(at, looking.Length).SequenceEqual(looking))
            {
                seen++;
            }
        }

        return seen;
    }

    private static byte[] Fragment(int mediaLength) => Joined(Box("moof", 100), Box("mdat", mediaLength));

    private static byte[] Box(string kind, int payloadLength)
    {
        byte[] box = new byte[8 + payloadLength];

        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(kind).CopyTo(box, 4);
        Array.Fill(box, (byte)payloadLength, 8, payloadLength);

        return box;
    }

    private static byte[] WideBox(string kind, int payloadLength)
    {
        byte[] box = new byte[16 + payloadLength];

        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes(kind).CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        Array.Fill(box, (byte)payloadLength, 16, payloadLength);

        return box;
    }

    private static byte[] Saying(string kind, uint size)
    {
        byte[] header = new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(header, size);
        Encoding.ASCII.GetBytes(kind).CopyTo(header, 4);

        return header;
    }

    private static byte[] WideSaying(string kind, long size)
    {
        byte[] header = new byte[16];

        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        Encoding.ASCII.GetBytes(kind).CopyTo(header, 4);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), (ulong)size);

        return header;
    }

    private static byte[] Joined(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
