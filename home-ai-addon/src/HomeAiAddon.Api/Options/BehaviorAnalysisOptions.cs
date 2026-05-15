namespace HomeAiAddon.Api.Options;

public sealed class BehaviorAnalysisOptions
{
    public const string SectionName = "BehaviorAnalysis";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8100";

    public int TimeoutSeconds { get; set; } = 30;

    public int EventLimit { get; set; } = 5000;

    public int MinSupport { get; set; } = 3;

    public double MinConfidence { get; set; } = 0.55;

    public int MaxGapSeconds { get; set; } = 300;

    public int MaxSequenceLength { get; set; } = 4;

    public int LookbackHours { get; set; } = 168;
}
