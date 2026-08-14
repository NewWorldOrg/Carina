using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class ScriptedTunerDetector(params TunerDetection[] detections) : ITunerDetector
{
    public int Detections { get; private set; }

    public IReadOnlyList<TunerDetection> Detect()
    {
        Detections++;

        return detections;
    }
}
