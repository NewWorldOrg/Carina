using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ChaptersCarriedTests
{
    private const string TwoChapters = """
        {
            "chapters": [
                { "id": 0, "time_base": "1/1000", "start": 0, "start_time": "0.000000", "end": 2500, "end_time": "2.500000", "tags": { "title": "本編" } },
                { "id": 1, "time_base": "1/1000", "start": 2500, "start_time": "2.500000", "end": 5750, "end_time": "5.750000", "tags": { "title": "CM" } }
            ]
        }
        """;

    private static readonly IReadOnlyList<ChapterSegment> Written =
    [
        new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), ChapterKind.Programme),
        new ChapterSegment(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5.75), ChapterKind.Break),
    ];

    [Fact(DisplayName = "the chapters ffprobe reads off a file are read with where they start and end and their titles, and a file with none carries none")]
    public void TheChaptersFfprobeReadsAreRead()
    {
        Assert.Equal(
            [
                new CarriedChapter(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), ChapterMetadataFile.ProgrammeTitle),
                new CarriedChapter(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5.75), ChapterMetadataFile.BreakTitle),
            ],
            ChaptersCarried.Read(TwoChapters));
        Assert.Empty(ChaptersCarried.Read("""{ "chapters": [] }""")!);
        Assert.Empty(ChaptersCarried.Read("{}")!);
        Assert.Null(ChaptersCarried.Read("not what ffprobe says"));
        Assert.Null(ChaptersCarried.Read("""{ "chapters": [ { "start_time": "soon" } ] }"""));
    }

    [Fact(DisplayName = "BR-CD-010: a copy carries the chapters written when there are as many, each where it was written within ten milliseconds and titled for its kind")]
    public void ACopyCarriesTheChaptersWrittenOrSaysWhyNot()
    {
        IReadOnlyList<CarriedChapter> carried = ChaptersCarried.Read(TwoChapters)!;

        Assert.Null(ChaptersCarried.Differs(carried, Written));
        Assert.Null(ChaptersCarried.Differs([], []));
        Assert.NotNull(ChaptersCarried.Differs(carried, [.. Written.Take(1)]));
        Assert.NotNull(ChaptersCarried.Differs(carried, [Written[0], new ChapterSegment(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5.77), ChapterKind.Break)]));
        Assert.NotNull(ChaptersCarried.Differs(carried, [Written[0], new ChapterSegment(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5.75), ChapterKind.Programme)]));
    }

    [Fact(DisplayName = "BR-CD-010: a copy carries an opening and an ending titled OP and ED, and one titled CM where an opening was written does not carry it")]
    public void ACopyCarriesAnOpeningAndAnEndingByTheirTitles()
    {
        IReadOnlyList<CarriedChapter> carried = ChaptersCarried.Read(
            """
            {
                "chapters": [
                    { "start_time": "0.000000", "end_time": "60.000000", "tags": { "title": "本編" } },
                    { "start_time": "60.000000", "end_time": "150.000000", "tags": { "title": "OP" } },
                    { "start_time": "150.000000", "end_time": "1710.000000", "tags": { "title": "本編" } },
                    { "start_time": "1710.000000", "end_time": "1800.000000", "tags": { "title": "ED" } }
                ]
            }
            """)!;
        ChapterSegment[] wanted =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(60), ChapterKind.Programme),
            new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(150), ChapterKind.Opening),
            new(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1710), ChapterKind.Programme),
            new(TimeSpan.FromSeconds(1710), TimeSpan.FromSeconds(1800), ChapterKind.Ending),
        ];

        Assert.Null(ChaptersCarried.Differs(carried, wanted));
        Assert.NotNull(ChaptersCarried.Differs([carried[0], carried[1] with { Title = ChapterMetadataFile.BreakTitle }, carried[2], carried[3]], wanted));
        Assert.NotNull(ChaptersCarried.Differs(carried, [wanted[0], wanted[1], wanted[2], new ChapterSegment(TimeSpan.FromSeconds(1710), TimeSpan.FromSeconds(1800), ChapterKind.Opening)]));
    }
}
