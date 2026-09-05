namespace Carina.Architecture.Tests;

public sealed class EncodeSettingRuleSelfCheckTests
{
    private const string Where = "Carina.Domain/Encodings/EncodeProfile.cs";

    public static TheoryData<string, string> EveryWayOfGivingASettingAFieldOfItsOwn => new()
    {
        {
            """
            public sealed class EncodeProfile
            {
                public string Extra { get; private set; }
            }
            """,
            "EncodeProfile.Extra string"
        },
        {
            """
            public sealed class EncodeProfile
            {
                public string? Extra { get; init; }
            }
            """,
            "EncodeProfile.Extra string?"
        },
        {
            """
            public sealed class EncodeProfile
            {
                public string Extra { get; }
            }
            """,
            "EncodeProfile.Extra string"
        },
        {
            """
            public sealed class EncodeProfile
            {
                public required string Extra { get; set; }
            }
            """,
            "EncodeProfile.Extra string"
        },
        {
            """
            public sealed class EncodeProfile
            {
                public string Extra;
            }
            """,
            "EncodeProfile.Extra string"
        },
        {
            """
            public sealed class EncodeProfile
            {
                public string Extra = "";
            }
            """,
            "EncodeProfile.Extra string"
        },
        {
            """
            public sealed record EncodeProfileDraft(
                string? Extra);
            """,
            "EncodeProfileDraft.Extra string?"
        },
        {
            """
            public sealed record EncodeProfileDraft(string? Extra);
            """,
            "EncodeProfileDraft.Extra string?"
        },
    };

    public static TheoryData<string> EveryWayOfWritingItThatWalksStraightPast =>
    [
        """
        public sealed class EncodeProfile
        {
            private string extra { get; set; }
        }
        """,
        """
        public sealed class EncodeProfile
        {
            internal string Extra { get; set; }
        }
        """,
        """
        public sealed class EncodeProfile
        {
            public static string Extra { get; set; }
        }
        """,
        """
        public sealed class EncodeProfile
        {
            public const string Extra = "";
        }
        """,
        """
        public sealed class EncodeProfile : CommonValueObject<string>
        {
        }
        """,
        """
        public sealed class EncodeProfile
        {
            public string Extra(int which) => which.ToString();
        }
        """,
    ];

    [Theory]
    [MemberData(nameof(EveryWayOfGivingASettingAFieldOfItsOwn))]
    public void DetectsThisWayOfGivingASettingAFieldOfItsOwn(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(Where, source);

        Assert.Equal([$"/{Where} {reported}"], EncodeSettingRules.WhatASettingKeeps(tree.Root, tree.Under(Where)));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfWritingItThatWalksStraightPast))]
    public void CannotSeeThisWayOfGivingASettingAFieldOfItsOwn(string source)
    {
        using var tree = new SourceTree();
        tree.Write(Where, source);

        Assert.Empty(EncodeSettingRules.WhatASettingKeeps(tree.Root, tree.Under(Where)));
        Assert.Empty(EncodeSettingRules.WhatASettingWorksOut(tree.Root, tree.Under(Where)));
    }

    [Fact]
    public void ReadsAFieldWorkedOutFromWhatIsKeptApartFromOneThatIsKept()
    {
        using var tree = new SourceTree();
        tree.Write(
            Where,
            """
            public sealed class EncodeProfile
            {
                public EncodeLabel Label { get; private set; }

                public string Wire => Label.Value;

                public bool Named { get => Label is not null; }
            }
            """);

        Assert.Equal(
            [$"/{Where} EncodeProfile.Label EncodeLabel"],
            EncodeSettingRules.WhatASettingKeeps(tree.Root, tree.Under(Where)));

        Assert.Equal(
            [$"/{Where} EncodeProfile.Named bool", $"/{Where} EncodeProfile.Wire string"],
            EncodeSettingRules.WhatASettingWorksOut(tree.Root, tree.Under(Where)));
    }

    [Fact]
    public void CannotTellAFieldOfANestedTypeApartFromOneOfTheTypeAroundIt()
    {
        using var tree = new SourceTree();
        tree.Write(
            Where,
            """
            public sealed class EncodeProfile
            {
                public sealed class Inner
                {
                    public string Extra { get; set; }
                }
            }
            """);

        Assert.Equal(
            [$"/{Where} Inner.Extra string"],
            EncodeSettingRules.WhatASettingKeeps(tree.Root, tree.Under(Where)));
    }

    [Theory]
    [InlineData("EncodeProfile.Label string", true)]
    [InlineData("EncodeProfile.Label string?", true)]
    [InlineData("EncodeProfile.Label EncodeLabel", false)]
    [InlineData("EncodeProfile.Label StringOfPearls", false)]
    public void TellsFreeTextApartFromATypeThatOnlyReadsLikeIt(string entry, bool free)
        => Assert.Equal(free, EncodeSettingRules.IsFreeText($"/{Where} {entry}"));

    [Fact]
    public void ReadsTheNameAndTheKindOutOfAnEntry()
    {
        Assert.Equal("Label", EncodeSettingRules.Named($"/{Where} EncodeProfile.Label EncodeLabel"));
        Assert.Equal("EncodeLabel", EncodeSettingRules.Kind($"/{Where} EncodeProfile.Label EncodeLabel"));
    }

    [Theory]
    [InlineData("public sealed record VideoBitrate(int KilobitsPerSecond);", "VideoBitrate")]
    [InlineData("public sealed record Kilobits(int PerSecond);", "Kilobits")]
    public void DetectsATypeNamedForABitrate(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(Where, source);

        Assert.Equal([reported], EncodeSettingRules.WhatIsNamedForABitrate(tree.Under(Where)));
    }

    [Fact]
    public void CannotSeeABitrateThatIsNotInTheNameOfAType()
    {
        using var tree = new SourceTree();
        tree.Write(
            Where,
            """
            public sealed class EncodeProfile
            {
                public int Bitrate { get; set; }
            }
            """);

        Assert.Empty(EncodeSettingRules.WhatIsNamedForABitrate(tree.Under(Where)));
    }

    [Fact]
    public void ReadsNothingOutOfAnEmptyTree()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Domain/Encodings/.keep", string.Empty);

        Assert.Empty(EncodeSettingRules.WhatASettingKeeps(tree.Root, tree.Under(Where)));
        Assert.Empty(EncodeSettingRules.WhatASettingWorksOut(tree.Root, tree.Under(Where)));
        Assert.Empty(EncodeSettingRules.WhatIsNamedForABitrate(tree.Under(Where)));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-setting-rules-");

        public string Root => directory.FullName;

        public string Under(string path)
            => Path.GetDirectoryName(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)))!;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
