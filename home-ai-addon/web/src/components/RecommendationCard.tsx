import { useState } from "react";
import {
  submitRecommendationFeedback,
  type FeedbackVerdict,
  type Recommendation
} from "../api/recommendations";

interface RecommendationCardProps {
  item: Recommendation;
  onFeedback?: (id: string, verdict: FeedbackVerdict) => void;
}

export function RecommendationCard({ item, onFeedback }: RecommendationCardProps) {
  const [submitting, setSubmitting] = useState(false);
  const [verdict, setVerdict] = useState<FeedbackVerdict | null>(null);
  const [error, setError] = useState<string | null>(null);

  const confidencePercent = Math.round(item.confidence * 100);
  const cadencePercent = Math.round(item.cadenceConfidence * 100);
  const learned = Math.abs(item.feedbackScore - 1) > 0.02;

  async function sendFeedback(next: FeedbackVerdict) {
    if (submitting || verdict) {
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      await submitRecommendationFeedback(item.id, {
        verdict: next,
        patternKey: item.patternKey,
        cadence: item.cadence,
        supportCount: item.supportCount,
        confidence: item.baseConfidence,
        frequencyScore: item.frequencyScore,
        entityIds: item.sequence.map((step) => step.entityId)
      });
      setVerdict(next);
      onFeedback?.(item.id, next);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <article className="event-item recommendation-card">
      <div className="recommendation-header">
        <h3 style={{ margin: 0, fontSize: "1.05rem" }}>{item.title}</h3>
        <span className="confidence-badge">{confidencePercent}%</span>
      </div>
      <div className="cadence-row">
        <span className="cadence-badge">{item.cadenceLabel}</span>
        {item.cadence !== "irregular" ? (
          <span className="muted" style={{ fontSize: 12 }}>
            {item.scheduleHint} · расписание {cadencePercent}%
          </span>
        ) : (
          <span className="muted" style={{ fontSize: 12 }}>
            {item.scheduleHint || "Без устойчивого расписания"}
          </span>
        )}
      </div>
      <p className="muted" style={{ margin: "8px 0" }}>
        {item.description}
      </p>
      <div className="confidence-bar-wrap" aria-label={`Уверенность ${confidencePercent}%`}>
        <div className="confidence-bar" style={{ width: `${confidencePercent}%` }} />
      </div>
      {learned ? (
        <p className="muted" style={{ fontSize: 12, marginTop: 6, marginBottom: 0 }}>
          Ранг скорректирован обучением на отзывах (×{item.feedbackScore.toFixed(2)})
        </p>
      ) : null}
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
      <div className="feedback-row">
        {verdict ? (
          <span className="feedback-thanks muted">
            {verdict === "useful"
              ? "Спасибо — похожие сценарии будут ранжироваться выше."
              : "Скрыто на несколько дней; похожие паттерны понизятся."}
          </span>
        ) : (
          <>
            <button
              type="button"
              className="feedback-btn feedback-btn-useful"
              disabled={submitting}
              onClick={() => void sendFeedback("useful")}
            >
              Полезно
            </button>
            <button
              type="button"
              className="feedback-btn feedback-btn-dismiss"
              disabled={submitting}
              onClick={() => void sendFeedback("not_useful")}
            >
              Не то
            </button>
          </>
        )}
      </div>
      {error ? (
        <p className="bad" style={{ fontSize: 12, marginTop: 8, marginBottom: 0 }}>
          {error}
        </p>
      ) : null}
    </article>
  );
}
