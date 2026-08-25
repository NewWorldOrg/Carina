using Carina.Contracts;

namespace Carina.Driver.Transport;

public sealed class PcrTimeline
{
    public const long WrapsAt = 8_589_934_592;

    public const long TicksPerSecond = 90_000;

    public const long RepeatedAtLeastEvery = TicksPerSecond / 10;

    public const long ContinuousWithin = RepeatedAtLeastEvery * 100;

    private readonly List<PcrReanchorDto> reanchors = [];

    private int followed;
    private int candidate;
    private bool watching;
    private long candidateFirst;
    private long? anchor;
    private long previous;
    private long elapsed;

    public long? Anchor => anchor;

    public bool Located => anchor is not null;

    public int Second => (int)(elapsed / TicksPerSecond);

    public IReadOnlyList<PcrReanchorDto> Reanchors => [.. reanchors];

    public void Observe(int pid, long reference, bool declaredDiscontinuous)
    {
        if (reference < 0 || reference >= WrapsAt)
        {
            return;
        }

        if (anchor is null)
        {
            followed = pid;
            anchor = reference;
            previous = reference;

            return;
        }

        if (pid != followed)
        {
            Consider(pid, reference);

            return;
        }

        watching = false;

        if (declaredDiscontinuous)
        {
            Reanchor(reference);

            return;
        }

        long step = reference - previous;
        if (step < 0 && step + WrapsAt <= ContinuousWithin)
        {
            step += WrapsAt;
        }

        if (step < 0 || step > ContinuousWithin)
        {
            Reanchor(reference);

            return;
        }

        elapsed += step;
        previous = reference;
    }

    private void Consider(int pid, long reference)
    {
        if (!watching || candidate != pid)
        {
            watching = true;
            candidate = pid;
            candidateFirst = reference;

            return;
        }

        long span = reference - candidateFirst;
        if (span < 0)
        {
            span += WrapsAt;
        }

        if (span <= ContinuousWithin)
        {
            return;
        }

        Reanchor(reference);
        followed = pid;
        watching = false;
    }

    private void Reanchor(long reference)
    {
        int second = Second;

        if (reanchors.Count > 0 && reanchors[^1].Second == second)
        {
            reanchors[^1] = reanchors[^1] with { After = reference };
        }
        else
        {
            reanchors.Add(new PcrReanchorDto(second, previous, reference));
        }

        previous = reference;
    }
}
