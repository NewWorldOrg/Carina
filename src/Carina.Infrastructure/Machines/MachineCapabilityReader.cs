using Carina.Domain.Machines;

namespace Carina.Infrastructure.Machines;

/// <summary>
/// Asks this machine what it can do and keeps the answer. Everything that wants to know — the live
/// path choosing an encoder, a job about to run — reads it from here. An answer in which a
/// question ran out of time is kept only for <see cref="MachineSettings.AskAgainAfterATimeOut"/>,
/// and the first read after that asks again; every other answer is kept for as long as the
/// process lives.
/// </summary>
public sealed class MachineCapabilityReader(MachineSettings settings, TimeProvider clock) : IMachineCapabilityReader
{
    private readonly Lock asking = new();

    private Task<Learned>? asked;

    public async Task<MachineCapabilities> ReadAsync(CancellationToken cancellationToken)
    {
        Task<Learned> answering;

        lock (asking)
        {
            if (asked is null || IsSpent(asked))
            {
                asked = LearnAsync();
            }

            answering = asked;
        }

        return (await answering.WaitAsync(cancellationToken)).Capabilities;
    }

    private bool IsSpent(Task<Learned> answer)
        => answer.IsFaulted
            || (answer.IsCompletedSuccessfully && answer.Result.KeptUntil is { } until && clock.GetUtcNow() >= until);

    private async Task<Learned> LearnAsync()
    {
        ProgrammeSaid encoders = await SayingAsync(FacultyInvocation.Encoders());
        ProgrammeSaid decoders = await SayingAsync(FacultyInvocation.Decoders());
        CardAnswer card = await AskTheCardAsync();
        bool cardEncodesH264 = CardStandings.IsUsable(card.Standing);
        ProgrammeSaid? h265 = cardEncodesH264
            ? await SayingAsync(VaapiProbeInvocation.Arguments(settings.RenderNode, FfmpegFaculties.H265OnTheCard))
            : null;
        bool cardEncodesH265 = h265 is { Ran: true, ExitCode: 0 };

        MachineCapabilities capabilities = MachineCapabilities.Of(
            card.Standing,
            FfmpegFaculties.Of(
                FfmpegFaculties.Listed(encoders.Said),
                FfmpegFaculties.Listed(decoders.Said),
                cardEncodesH264,
                cardEncodesH265),
            Together(card.Note, encoders.Ran ? string.Empty : encoders.Complained));

        bool ranOutOfTime = card.Standing is CardStanding.ProbeTimedOut
            || new[] { encoders, decoders, h265 }.Any(said => said?.Fault is ProgrammeFault.TimedOut);

        return new Learned(capabilities, ranOutOfTime ? clock.GetUtcNow() + settings.AskAgainAfterATimeOut : null);
    }

    private async Task<CardAnswer> AskTheCardAsync()
    {
        if (OutOfReach() is { } absent)
        {
            return absent;
        }

        ProgrammeSaid probe = await SayingAsync(VaapiProbeInvocation.Arguments(settings.RenderNode));

        return probe.Fault switch
        {
            ProgrammeFault.ProgrammeMissing => new CardAnswer(CardStanding.ProbeProgrammeMissing, probe.Complained),
            ProgrammeFault.TimedOut => new CardAnswer(CardStanding.ProbeTimedOut, probe.Complained),
            _ => probe.ExitCode is 0
                ? new CardAnswer(CardStanding.Usable, string.Empty)
                : new CardAnswer(CardStanding.DriverUnusable, probe.Complained),
        };
    }

    private CardAnswer? OutOfReach()
    {
        try
        {
            using FileStream node = File.Open(settings.RenderNode, FileMode.Open, FileAccess.ReadWrite);

            return null;
        }
        catch (Exception absent) when (absent is FileNotFoundException or DirectoryNotFoundException)
        {
            return new CardAnswer(CardStanding.NodeMissing, "no render node was handed to this container");
        }
        catch (UnauthorizedAccessException)
        {
            return new CardAnswer(
                CardStanding.NodeUnreadable,
                "the render node is there and this process is not in the group that may open it");
        }
        catch (IOException failure)
        {
            return new CardAnswer(CardStanding.NodeUnreadable, failure.Message);
        }
    }

    private Task<ProgrammeSaid> SayingAsync(IReadOnlyList<string> arguments)
        => AnotherProgramme.SayAsync(
            settings.Programme,
            arguments,
            settings.LongestProbe,
            clock,
            CancellationToken.None);

    private static string Together(string card, string listing)
        => (card.Length, listing.Length) switch
        {
            (0, 0) => string.Empty,
            (0, _) => listing,
            (_, 0) => card,
            _ => $"{card}; {listing}",
        };

    private readonly record struct CardAnswer(CardStanding Standing, string Note);

    private sealed record Learned(MachineCapabilities Capabilities, DateTimeOffset? KeptUntil);
}
