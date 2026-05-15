namespace HomeAiAddon.Api.Data.Entities;

public sealed class AnomalyAlertRecord
{
    public long Id { get; set; }

    public string DetectionId { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string AnomalyType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public double Score { get; set; }

    public string Method { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    public DateTimeOffset DetectedAtUtc { get; set; }

    public DateTimeOffset PersistedAtUtc { get; set; }

    public string RelatedEventIdsJson { get; set; } = "[]";

    public string MetricsJson { get; set; } = "{}";
}
