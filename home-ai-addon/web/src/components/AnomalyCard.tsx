import type { AnomalyAlert } from "../api/anomalies";

interface AnomalyCardProps {
  item: AnomalyAlert;
}

const SEVERITY_LABELS: Record<string, string> = {
  low: "Низкая",
  medium: "Средняя",
  high: "Высокая"
};

const TYPE_LABELS: Record<string, string> = {
  activity_spike: "Всплеск активности",
  unusual_time: "Нетипичное время",
  consumption_spike: "Скачок потребления",
  device_behavior: "Поведение устройства"
};

const METHOD_LABELS: Record<string, string> = {
  z_score: "Z-score",
  rolling_average: "Скользящее среднее",
  isolation_forest: "Isolation Forest",
  hour_histogram: "Распределение по часам"
};

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString("ru-RU", { timeZone: "UTC" }) + " UTC";
  } catch {
    return iso;
  }
}

export function AnomalyCard({ item }: AnomalyCardProps) {
  const scorePercent = Math.round(item.score * 100);
  const severityLabel = SEVERITY_LABELS[item.severity] ?? item.severity;
  const typeLabel = TYPE_LABELS[item.anomalyType] ?? item.anomalyType;
  const methodLabel = METHOD_LABELS[item.method] ?? item.method;

  return (
    <article className={`event-item anomaly-card anomaly-severity-${item.severity}`}>
      <div className="recommendation-header">
        <h3 style={{ margin: 0, fontSize: "1.05rem" }}>{item.title}</h3>
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          <span className={`severity-badge severity-${item.severity}`}>{severityLabel}</span>
          <span className="confidence-badge">{scorePercent}%</span>
        </div>
      </div>
      <div className="cadence-row">
        <span className="cadence-badge">{typeLabel}</span>
        <span className="muted" style={{ fontSize: 12 }}>
          {methodLabel}
        </span>
      </div>
      <p className="muted" style={{ margin: "8px 0" }}>
        {item.explanation}
      </p>
      <div className="confidence-bar-wrap" aria-label={`Оценка аномалии ${scorePercent}%`}>
        <div
          className={`confidence-bar anomaly-score-bar severity-bar-${item.severity}`}
          style={{ width: `${scorePercent}%` }}
        />
      </div>
      <div className="mono" style={{ fontSize: 12, marginTop: 10 }}>
        {item.entityId} · {formatDate(item.detectedAtUtc)}
        {item.relatedEventIds.length > 0
          ? ` · события: ${item.relatedEventIds.slice(0, 5).join(", ")}${
              item.relatedEventIds.length > 5 ? "…" : ""
            }`
          : ""}
      </div>
      {Object.keys(item.metrics).length > 0 ? (
        <details style={{ marginTop: 10 }}>
          <summary className="muted">Метрики детектора</summary>
          <pre className="mono" style={{ marginTop: 8 }}>
            {JSON.stringify(item.metrics, null, 2)}
          </pre>
        </details>
      ) : null}
    </article>
  );
}
