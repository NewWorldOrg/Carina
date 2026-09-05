namespace Carina.Domain.Machines;

public sealed record MachineSettings
{
    public const string TheRenderNode = "/dev/dri/renderD128";

    public string Programme { get; init; } = "ffmpeg";

    public string RenderNode { get; init; } = TheRenderNode;

    public TimeSpan LongestProbe { get; init; } = TimeSpan.FromSeconds(10);
}
