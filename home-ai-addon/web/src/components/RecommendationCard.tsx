import type { Recommendation } from "../api/recommendations";

interface RecommendationCardProps {
  item: Recommendation;
}

export function RecommendationCard({ item }: RecommendationCardProps) {
  const confidencePercent = Math.round(item.confidence * 100);

  return (
    <article className="event-item recommendation-card">
      <div className="recommendation-header">
        <h3 style={{ margin: 0, fontSize: "1.05rem" }}>{item.title}</h3>
        <span className="confidence-badge">{confidencePercent}%</span>
      </div>
      <p className="muted" style={{ margin: "8px 0" }}>
        {item.description}
      </p>
      <div className="confidence-bar-wrap" aria-label={`Уверенность ${confidencePercent}%`}>
        <div
          className="confidence-bar"
          style={{ width: `${confidencePercent}%` }}
        />
      </div>
      <div className="mono" style={{ fontSize: 12, marginTop: 10 }}>
        Поддержка: {item.supportCount} · Сессий: {item.sessionCount} · Частота:{" "}
        {Math.round(item.frequencyScore * 100)}%
      </div>
      <ol className="sequence-steps">
        {item.sequence.map((step, index) => (
          <li key={`${item.id}-${index}`}>
            <span>{step.label}</span>
            <span className="muted mono">{step.entityId}</span>
          </li>
        ))}
      </ol>
      <details style={{ marginTop: 10 }}>
        <summary className="muted">Предлагаемая автоматизация</summary>
        <pre className="mono" style={{ marginTop: 8 }}>
          {JSON.stringify(item.suggestedAutomation, null, 2)}
        </pre>
      </details>
    </article>
  );
}
