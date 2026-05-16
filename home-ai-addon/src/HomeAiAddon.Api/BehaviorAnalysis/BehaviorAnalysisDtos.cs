namespace HomeAiAddon.Api.BehaviorAnalysis;

public sealed record AnalyzeRequestPayload(
    IReadOnlyList<AnalyzeEventPayload> Events,
    AnalyzeOptionsPayload? Options);

public sealed record AnalyzeEventPayload(
    long Id,
    string EntityId,
    string? OldState,
    string? NewState,
    string? FriendlyName,
    DateTimeOffset TimeFiredUtc,
    DateTimeOffset ReceivedAtUtc,
    string? ContextId = null,
    string? ContextUserId = null,
    string? ContextParentId = null,
    string? Domain = null,
    string? DeviceClass = null,
    string? UnitOfMeasurement = null,
    string? EntityCategory = null,
    long? SupportedFeatures = null,
    string? AreaId = null,
    string? AreaName = null);

public sealed record AnalyzeOptionsPayload(
    int MinSupport,
    double MinConfidence,
    double MinCadenceConfidence,
    bool RequirePeriodic,
    int MaxGapSeconds,
    int MaxSequenceLength,
    int LookbackHours,
    int FeedbackDismissDays = 14,
    double MinLift = 1.2,
    double MinSupportRatio = 0.03,
    int MaxStepGapSeconds = 180);

public sealed record ExplanationFactorPayload(
    string Key,
    string Label,
    string Value,
    double Weight,
    double Score);

public sealed record AnalyzeResponsePayload(
    int AnalyzedEventCount,
    int SessionCount,
    int PatternCandidates,
    int FeedbackTrainingSamples,
    IReadOnlyList<RecommendationPayload> Recommendations,
    IReadOnlyDictionary<string, object>? OptionsUsed);

public sealed record DiagnosticsCounterPayload(string Key, int Count);

public sealed record DiagnosticsResponsePayload(
    int AnalyzedEventCount,
    int EligibleEventCount,
    int SessionCount,
    int RawSequenceCandidateCount,
    int SemanticRejectedCandidateCount,
    int SensorToSensorCandidateCount,
    int MeaningfulCandidateCount,
    int QualityFilteredCandidateCount,
    int RecommendationCount,
    IReadOnlyList<DiagnosticsCounterPayload> FilterReasons,
    IReadOnlyList<DiagnosticsCounterPayload> SemanticRoles,
    IReadOnlyList<DiagnosticsCounterPayload> SemanticIntents,
    IReadOnlyDictionary<string, object>? OptionsUsed);

public sealed record RecommendationPayload(
    string Id,
    string PatternKey,
    IReadOnlyList<SequenceStepPayload> Sequence,
    int SupportCount,
    int SessionCount,
    double Confidence,
    double BaseConfidence,
    double FeedbackScore,
    double FrequencyScore,
    double Lift,
    double SupportRatio,
    string Cadence,
    double CadenceConfidence,
    string CadenceLabel,
    string ScheduleHint,
    string Title,
    string Description,
    string WhyGenerated,
    IReadOnlyList<ExplanationFactorPayload> ExplanationFactors,
    IReadOnlyList<double> MedianStepGapsSeconds,
    string? WeekdayHint,
    SuggestedAutomationPayload SuggestedAutomation);

public sealed record FeedbackRequestPayload(
    string RecommendationId,
    string PatternKey,
    string Verdict,
    string Cadence,
    int SupportCount,
    double Confidence,
    double FrequencyScore,
    IReadOnlyList<string> EntityIds);

public sealed record FeedbackResponsePayload(
    bool Accepted,
    int TrainingSamples,
    string Message);

public sealed record FeedbackResetItemsRequestPayload(
    IReadOnlyList<string> PatternKeys,
    IReadOnlyList<string> RecommendationIds,
    IReadOnlyList<string> EntityIds,
    bool ClearPositive = false,
    bool ClearNegative = true,
    bool ClearDismissals = true);

public sealed record FeedbackStatePayload(
    int TrainingSamples,
    IReadOnlyDictionary<string, int> PatternUseful,
    IReadOnlyDictionary<string, int> PatternNotUseful,
    IReadOnlyDictionary<string, int> EntityUseful,
    IReadOnlyDictionary<string, int> EntityNotUseful,
    IReadOnlyDictionary<string, string> DismissedUntil);

public sealed record SequenceStepPayload(
    string Label,
    string EntityId,
    string? NewState,
    string? FriendlyName,
    string? AreaId,
    string? AreaName);

public sealed record SuggestedAutomationPayload(
    string TriggerEntityId,
    string? TriggerToState,
    IReadOnlyList<string> ActionEntityIds,
    IReadOnlyList<string?> ActionToStates);

public sealed record MlHealthPayload(string Status, string Service);

public sealed record AnomalyDetectRequestPayload(
    IReadOnlyList<AnalyzeEventPayload> Events,
    AnomalyDetectionOptionsPayload? Options);

public sealed record AnomalyDetectionOptionsPayload(
    int MinEvents,
    int MinEventsPerEntity,
    int MinHourlySamples,
    int RollingWindowHours,
    double ZScoreThreshold,
    double UnusualHourMaxRatio,
    double MinNumericDelta,
    double MinScore,
    double MediumSeverityThreshold,
    double HighSeverityThreshold,
    int MaxResults,
    int IsolationForestEstimators,
    double IsolationForestContamination,
    int IsolationForestMinSamples);

public sealed record AnomalyDetectResponsePayload(
    int AnalyzedEventCount,
    int AnomalyCount,
    IReadOnlyList<AnomalyItemPayload> Anomalies,
    IReadOnlyDictionary<string, object>? OptionsUsed);

public sealed record AnomalyItemPayload(
    string Id,
    string EntityId,
    string AnomalyType,
    string Severity,
    double Score,
    string Method,
    string Title,
    string Explanation,
    DateTimeOffset DetectedAtUtc,
    IReadOnlyList<long> RelatedEventIds,
    IReadOnlyDictionary<string, object> Metrics);
