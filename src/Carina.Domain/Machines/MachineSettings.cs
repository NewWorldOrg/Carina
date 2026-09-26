namespace Carina.Domain.Machines;

public sealed record MachineSettings
{
    public const string TheRenderNode = "/dev/dri/renderD128";

    public string Programme { get; init; } = "ffmpeg";

    public string Prober { get; init; } = "ffprobe";

    public string RenderNode { get; init; } = TheRenderNode;

    public int Cores { get; init; } = Environment.ProcessorCount;

    public TimeSpan LongestProbe { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan AskAgainAfterATimeOut { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan LongestRead { get; init; } = TimeSpan.FromSeconds(60);
}
