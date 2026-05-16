namespace HomeAiAddon.Api.Options;

public sealed class AnomalyDetectionOptions
{
    public const string SectionName = "AnomalyDetection";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 15;

    public int EventLimit { get; set; } = 3000;

    public int RetentionDays { get; set; } = 30;

    public int ListLimit { get; set; } = 100;

    public int MinEvents { get; set; } = 50;

    public int MinEventsPerEntity { get; set; } = 8;

    public int MinHourlySamples { get; set; } = 4;

    public int RollingWindowHours { get; set; } = 24;

    public double ZScoreThreshold { get; set; } = 2.5;

    public double UnusualHourMaxRatio { get; set; } = 0.05;

    public double MinNumericDelta { get; set; } = 0.5;

    public double MinScore { get; set; } = 0.55;

    public double MediumSeverityThreshold { get; set; } = 0.7;

    public double HighSeverityThreshold { get; set; } = 0.85;

    public int MaxResults { get; set; } = 30;

    public int IsolationForestEstimators { get; set; } = 50;

    public double IsolationForestContamination { get; set; } = 0.08;

    public int IsolationForestMinSamples { get; set; } = 20;
}
