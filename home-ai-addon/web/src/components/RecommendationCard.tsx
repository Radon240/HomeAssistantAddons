import { useState } from "react";
import {
  submitRecommendationFeedback,
  type ExplanationFactor,
  type FeedbackVerdict,
  type Recommendation
} from "../api/recommendations";

interface RecommendationCardProps {
  item: Recommendation;
  onFeedback?: (id: string, verdict: FeedbackVerdict) => void;
}

function buildFeedbackPayload(item: Recommendation) {
  const patternKey =
    item.patternKey?.trim() ||
    item.sequence.map((step) => step.entityId).join("|") ||
    item.sequence.map((step) => step.label).join("|");

  return {
    patternKey,
    cadence: item.cadence || "irregular",
    supportCount: item.supportCount ?? 0,
    confidence: item.baseConfidence ?? item.confidence ?? 0,
    frequencyScore: item.frequencyScore ?? 0,
    entityIds: item.sequence.map((step) => step.entityId).filter(Boolean)
  };
}

function ConfidenceBarFill({ percent }: { percent: number }) {
  return <div className="confidence-bar" style={{ width: `${percent}%` }} />;
}

function ConfidenceBarWrap({ percent }: { percent: number }) {
  return (
    <div className="confidence-bar-wrap" aria-label={`Уверенность ${percent}%`}>
      <ConfidenceBarFill percent={percent} />
    </div>
  );
}

function ExplanationFactorContent({
  factor,
  percent
}: {
  factor: ExplanationFactor;
  percent: number;
}) {
  return (
    <div className="explanation-factor">
      <div style={{ display: "flex", justifyContent: "space-between", gap: 8, fontSize: 12 }}>
        <span>
          <strong>{factor.label}:</strong> {factor.value}
        </span>
        <span className="muted">{percent}%</span>
      </div>
      <div className="confidence-bar-wrap" style={{ height: 6, marginTop: 4 }}>
        <ConfidenceBarFill percent={percent} />
      </div>
    </div>
  );
}

function ExplanationFactorRow({ factor }: { factor: ExplanationFactor }) {
  const percent = Math.round(factor.score * 100);
  return (
    <li>
      <ExplanationFactorContent factor={factor} percent={percent} />
    </li>
  );
}

function CadenceRow({
  item,
  cadencePercent,
  lift
}: {
  item: Recommendation;
  cadencePercent: number;
  lift: number;
}) {
  return (
    <div className="cadence-row">
      <span className="cadence-badge">{item.cadenceLabel}</span>
      {item.cadence !== "irregular" ? (
        <span className="muted" style={{ fontSize: 12 }}>
          {item.scheduleHint} · расписание {cadencePercent}%
        </span>
      ) : item.weekdayHint ? (
        <span className="muted" style={{ fontSize: 12 }}>
          {item.weekdayHint}
        </span>
      ) : (
        <span className="muted" style={{ fontSize: 12 }}>
          {item.scheduleHint || "Без устойчивого расписания"}
        </span>
      )}
      {lift >= 1.2 ? (
        <span className="muted" style={{ fontSize: 12 }}>
          · lift {lift.toFixed(1)}×
        </span>
      ) : null}
    </div>
  );
}

function RecommendationFeedback({
  verdict,
  submitting,
  onUseful,
  onDismiss
}: {
  verdict: FeedbackVerdict | null;
  submitting: boolean;
  onUseful: () => void;
  onDismiss: () => void;
}) {
  return (
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
            onClick={onUseful}
          >
            Полезно
          </button>
          <button
            type="button"
            className="feedback-btn feedback-btn-dismiss"
            disabled={submitting}
            onClick={onDismiss}
          >
            Не то
          </button>
        </>
      )}
    </div>
  );
}

function StatsLine({ item }: { item: Recommendation }) {
  return (
    <div className="mono" style={{ fontSize: 12, marginTop: 10 }}>
      Поддержка: {item.supportCount} · Сессий: {item.sessionCount} · Частота:{" "}
      {Math.round(item.frequencyScore * 100)}%
      {item.medianStepGapsSeconds?.length
        ? ` · паузы: ${item.medianStepGapsSeconds.map((g) => `${Math.round(g)}с`).join(", ")}`
        : ""}
    </div>
  );
}

export function RecommendationCard({ item, onFeedback }: RecommendationCardProps) {
  const [submitting, setSubmitting] = useState(false);
  const [verdict, setVerdict] = useState<FeedbackVerdict | null>(null);
  const [error, setError] = useState<string | null>(null);

  const confidencePercent = Math.round(item.confidence * 100);
  const cadencePercent = Math.round(item.cadenceConfidence * 100);
  const feedbackScore = typeof item.feedbackScore === "number" ? item.feedbackScore : 1;
  const learned = Math.abs(feedbackScore - 1) > 0.02;
  const lift = typeof item.lift === "number" ? item.lift : 0;
  const factors = item.explanationFactors ?? [];
  const isStub = item.isStub === true;

  async function sendFeedback(next: FeedbackVerdict) {
    if (submitting || verdict) {
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      if (!isStub) {
        await submitRecommendationFeedback(item.id, {
          verdict: next,
          ...buildFeedbackPayload(item)
        });
      }
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
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          {isStub ? <span className="muted">Демо</span> : null}
          <span className="confidence-badge">{confidencePercent}%</span>
        </div>
      </div>
      <CadenceRow item={item} cadencePercent={cadencePercent} lift={lift} />
      {item.whyGenerated ? (
        <p className="muted" style={{ margin: "8px 0 0", fontSize: 13 }}>
          <strong style={{ fontWeight: 600 }}>Почему предложено:</strong> {item.whyGenerated}
        </p>
      ) : null}
      {item.areaHint ? (
        <p className="muted" style={{ margin: "6px 0 0", fontSize: 13 }}>
          <strong style={{ fontWeight: 600 }}>Зона:</strong> {item.areaHint}
        </p>
      ) : null}
      <p className="muted" style={{ margin: "8px 0" }}>
        {item.description}
      </p>
      <ConfidenceBarWrap percent={confidencePercent} />
      {learned ? (
        <p className="muted" style={{ fontSize: 12, marginTop: 6, marginBottom: 0 }}>
          Ранг скорректирован обучением на отзывах (×{feedbackScore.toFixed(2)})
        </p>
      ) : null}
      {factors.length > 0 ? (
        <details style={{ marginTop: 12 }} open>
          <summary className="muted">Факторы уверенности</summary>
          <ul className="explanation-factors">
            {factors.map((factor) => (
              <ExplanationFactorRow key={factor.key} factor={factor} />
            ))}
          </ul>
        </details>
      ) : null}
      <StatsLine item={item} />
      <ol className="sequence-steps">
        {item.sequence.map((step, index) => (
          <li key={`${item.id}-${index}`}>
            <span>{step.label}</span>
            <span className="muted mono">
              {step.entityId}
              {step.areaName ? ` · ${step.areaName}` : ""}
            </span>
            <span className="muted" style={{ fontSize: 12 }}>
              origin: {step.origin ?? "unknown"} · intent {Math.round((step.intentScore ?? 0) * 100)}% ·
              state {Math.round((step.stateImportance ?? 0) * 100)}% · weight{" "}
              {Math.round((step.eventWeight ?? 0) * 100)}%
            </span>
            {step.contextParentId ? (
              <span className="muted mono" style={{ fontSize: 11 }}>
                parent context: {step.contextParentId}
              </span>
            ) : null}
            {step.intelligenceExplanation ? (
              <span className="muted" style={{ fontSize: 11 }}>
                {step.intelligenceExplanation}
              </span>
            ) : null}
          </li>
        ))}
      </ol>
      <details style={{ marginTop: 10 }}>
        <summary className="muted">Предлагаемая автоматизация</summary>
        <pre className="mono" style={{ marginTop: 8 }}>
          {JSON.stringify(item.suggestedAutomation, null, 2)}
        </pre>
      </details>
      {isStub ? (
        <p className="muted" style={{ fontSize: 12, marginTop: 10, marginBottom: 0 }}>
          Это демонстрационная карточка для показа интерфейса.
        </p>
      ) : (
        <RecommendationFeedback
          verdict={verdict}
          submitting={submitting}
          onUseful={() => void sendFeedback("useful")}
          onDismiss={() => void sendFeedback("not_useful")}
        />
      )}
      {error ? (
        <p className="bad" style={{ fontSize: 12, marginTop: 8, marginBottom: 0 }}>
          {error}
        </p>
      ) : null}
    </article>
  );
}
