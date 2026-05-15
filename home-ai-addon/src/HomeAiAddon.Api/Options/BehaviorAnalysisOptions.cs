namespace HomeAiAddon.Api.Options;

public sealed class BehaviorAnalysisOptions
{
    public const string SectionName = "BehaviorAnalysis";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8100";

    /// <summary>Start uvicorn from .NET (local Development). Disabled in Production/Docker.</summary>
    public bool AutoStart { get; set; }

    public int StartupWaitSeconds { get; set; } = 45;

    public string? PythonExecutable { get; set; }

    public string? MlServiceDirectory { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public int EventLimit { get; set; } = 5000;

    public int MinSupport { get; set; } = 3;

    public double MinConfidence { get; set; } = 0.55;

    public double MinCadenceConfidence { get; set; } = 0.4;

    public bool RequirePeriodic { get; set; }

    public int MaxGapSeconds { get; set; } = 300;

    public int MaxSequenceLength { get; set; } = 4;

    public int LookbackHours { get; set; } = 336;
}
