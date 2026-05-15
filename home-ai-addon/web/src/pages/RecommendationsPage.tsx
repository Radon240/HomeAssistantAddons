import { useCallback, useEffect, useState } from "react";
import { AnalysisExclusionsPanel } from "../components/AnalysisExclusionsPanel";
import { RecommendationCard } from "../components/RecommendationCard";
import {
  fetchRecommendations,
  type RecommendationsResponse
} from "../api/recommendations";

export function RecommendationsPage() {
  const [data, setData] = useState<RecommendationsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await fetchRecommendations();
      setData(response);
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
      <section className="card">
        <div style={{ display: "flex", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
          <div>
            <h2 style={{ marginTop: 0 }}>Рекомендации</h2>
            <p className="muted" style={{ margin: 0 }}>
              Повторяющиеся сценарии с расписанием: каждый час, ежедневно, еженедельно и др.
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
        <RecommendationCard key={item.id} item={item} />
      ))}
    </div>
  );
}
