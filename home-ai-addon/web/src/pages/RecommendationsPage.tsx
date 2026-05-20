import { useCallback, useEffect, useState } from "react";
import { AnalysisExclusionsPanel } from "../components/AnalysisExclusionsPanel";
import { FeedbackLearningPanel } from "../components/FeedbackLearningPanel";
import { RecommendationCard } from "../components/RecommendationCard";
import {
  fetchRecommendations,
  type Recommendation,
  type RecommendationsResponse
} from "../api/recommendations";

function buildStubRecommendation(): Recommendation {
  return {
    id: "stub-evening-light-scene",
    isStub: true,
    patternKey: "light.living_room_main|media_player.tv_living_room",
    sequence: [
      {
        label: "Включается свет в гостиной",
        entityId: "light.living_room_main",
        newState: "on",
        friendlyName: "Свет в гостиной",
        areaId: "living_room",
        areaName: "Гостиная",
        contextId: null,
        contextUserId: null,
        contextParentId: null,
        origin: "LOCAL",
        intentScore: 0.87,
        stateImportance: 0.76,
        eventWeight: 0.81,
        intelligenceExplanation: "Типичный старт вечернего сценария."
      },
      {
        label: "Запускается ТВ",
        entityId: "media_player.tv_living_room",
        newState: "on",
        friendlyName: "Телевизор",
        areaId: "living_room",
        areaName: "Гостиная",
        contextId: null,
        contextUserId: null,
        contextParentId: "light.living_room_main",
        origin: "LOCAL",
        intentScore: 0.78,
        stateImportance: 0.7,
        eventWeight: 0.75,
        intelligenceExplanation: "Часто следует сразу после включения света."
      }
    ],
    supportCount: 9,
    sessionCount: 14,
    confidence: 0.79,
    baseConfidence: 0.79,
    feedbackScore: 1,
    frequencyScore: 0.64,
    lift: 1.9,
    supportRatio: 0.64,
    cadence: "evening",
    cadenceConfidence: 0.72,
    cadenceLabel: "Вечерний паттерн",
    scheduleHint: "Обычно с 19:00 до 22:00",
    title: "Сцена «Вечер в гостиной»",
    description:
      "Можно создать автоматизацию: при включении света в гостиной запускать комфортную вечернюю сцену с ТВ и мягким освещением.",
    whyGenerated:
      "События часто повторяются в одном порядке и времени, с высоким lift по сравнению со случайными совпадениями.",
    explanationFactors: [
      { key: "sequence", label: "Стабильность последовательности", value: "Высокая", weight: 0.35, score: 0.83 },
      { key: "cadence", label: "Повторяемость по времени", value: "Вечером", weight: 0.3, score: 0.72 },
      { key: "support", label: "Поддержка в сессиях", value: "9 из 14", weight: 0.35, score: 0.64 }
    ],
    medianStepGapsSeconds: [42],
    weekdayHint: "Чаще в будни",
    areaHint: "Гостиная",
    suggestedAutomation: {
      triggerEntityId: "light.living_room_main",
      triggerToState: "on",
      actionEntityIds: ["light.living_room_ambient", "media_player.tv_living_room"],
      actionToStates: ["on", "on"]
    }
  };
}

export function RecommendationsPage() {
  const [data, setData] = useState<RecommendationsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await fetchRecommendations();
      if (response.recommendations.length === 0) {
        setData({
          ...response,
          recommendations: [buildStubRecommendation()],
          message:
            response.message ??
            "Пока нет реальных автоматизаций. Ниже показана демонстрационная карточка рекомендации."
        });
      } else {
        setData(response);
      }
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="row">
      <AnalysisExclusionsPanel onSaved={() => void load()} />
      <FeedbackLearningPanel onChanged={() => void load()} />
      <section className="card">
        <div style={{ display: "flex", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
          <div>
            <h2 style={{ marginTop: 0 }}>Рекомендации</h2>
            <p className="muted" style={{ margin: 0 }}>
              Повторяющиеся сценарии с учётом последовательности, времени, lift и объяснимой
              уверенностью.
            </p>
          </div>
          <button type="button" onClick={() => void load()} disabled={loading}>
            {loading ? "Анализ…" : "Обновить"}
          </button>
        </div>
        {data ? (
          <p className="muted" style={{ marginBottom: 0, marginTop: 12 }}>
            В модель: {data.analyzedEventCount} событий (просмотрено {data.scannedEventCount},
            исключено {data.excludedEventCount}) · Сессий: {data.sessionCount} · Кандидатов:{" "}
            {data.patternCandidates}
            {data.feedbackTrainingSamples > 0
              ? ` · Обучение на отзывах: ${data.feedbackTrainingSamples} прим.`
              : ""}
          </p>
        ) : null}
      </section>

      {error ? (
        <section className="card bad">
          <strong>Ошибка</strong>
          <div className="mono" style={{ marginTop: 8 }}>
            {error}
          </div>
        </section>
      ) : null}

      {data?.message ? (
        <section className="card">
          <p className="muted" style={{ margin: 0 }}>
            {data.message}
          </p>
        </section>
      ) : null}

      {loading && !data ? (
        <section className="card">
          <p className="muted" style={{ margin: 0 }}>
            Загрузка рекомендаций…
          </p>
        </section>
      ) : null}

      {data && data.recommendations.length === 0 && !data.message ? (
        <section className="card">
          <p className="muted" style={{ margin: 0 }}>
            Повторяющиеся сценарии пока не обнаружены. Накопите больше событий или снизьте пороги в конфигурации.
          </p>
        </section>
      ) : null}

      {data?.recommendations.map((item) => (
        <RecommendationCard
          key={item.id}
          item={item}
          onFeedback={(id, verdict) => {
            if (verdict === "not_useful") {
              setData((prev) =>
                prev
                  ? {
                      ...prev,
                      recommendations: prev.recommendations.filter((r) => r.id !== id)
                    }
                  : prev
              );
            }
          }}
        />
      ))}
    </div>
  );
}
