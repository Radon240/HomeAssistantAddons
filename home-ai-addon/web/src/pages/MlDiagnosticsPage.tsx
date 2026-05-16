import { useEffect, useMemo, useState } from "react";
import {
  DiagnosticsCounter,
  RecommendationDiagnosticsResponse,
  fetchRecommendationDiagnostics
} from "../api/recommendations";

function StatCard({ label, value, hint }: { label: string; value: number; hint?: string }) {
  return (
    <div className="diagnostic-stat">
      <div className="muted">{label}</div>
      <strong>{value}</strong>
      {hint ? <small className="muted">{hint}</small> : null}
    </div>
  );
}

function CounterList({ title, items }: { title: string; items: DiagnosticsCounter[] }) {
  return (
    <section className="card">
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {items.length === 0 ? (
        <p className="muted" style={{ marginBottom: 0 }}>
          Нет данных.
        </p>
      ) : (
        <ul className="diagnostic-list">
          {items.map((item) => (
            <li key={item.key}>
              <span className="mono">{item.key}</span>
              <strong>{item.count}</strong>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

export function MlDiagnosticsPage() {
  const [data, setData] = useState<RecommendationDiagnosticsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setData(await fetchRecommendationDiagnostics());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось загрузить диагностику");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const suppressedTotal = useMemo(() => {
    if (!data) {
      return 0;
    }
    return data.semanticRejectedCandidateCount + data.qualityFilteredCandidateCount;
  }, [data]);

  return (
    <div className="row">
      <section className="card">
        <div className="recommendation-header">
          <div>
            <h2 style={{ marginTop: 0 }}>Диагностика ML</h2>
            <p className="muted" style={{ marginBottom: 0 }}>
              Показывает, почему система предлагает мало рекомендаций или подавляет кандидатов.
            </p>
          </div>
          <button type="button" onClick={() => void load()} disabled={loading}>
            {loading ? "Обновление..." : "Обновить"}
          </button>
        </div>
      </section>

      {error ? <section className="card bad">{error}</section> : null}
      {data?.message ? <section className="card muted">{data.message}</section> : null}

      {data ? (
        <>
          <section className="diagnostic-grid">
            <StatCard label="Просканировано событий" value={data.scannedEventCount} />
            <StatCard label="Передано в ML" value={data.analyzedEventCount} />
            <StatCard label="Прошло semantic layer" value={data.eligibleEventCount} />
            <StatCard label="Исключено UI/config" value={data.excludedEventCount} />
            <StatCard label="Сессий поведения" value={data.sessionCount} />
            <StatCard label="Сырых sequence-кандидатов" value={data.rawSequenceCandidateCount} />
            <StatCard label="Отброшено семантикой" value={data.semanticRejectedCandidateCount} />
            <StatCard label="sensor -> sensor" value={data.sensorToSensorCandidateCount} />
            <StatCard label="automation/cascade" value={data.automationGeneratedCandidateCount} />
            <StatCard label="Отброшено quality gates" value={data.qualityFilteredCandidateCount} />
            <StatCard label="Финальных рекомендаций" value={data.recommendationCount} />
          </section>

          <section className="card">
            <h3 style={{ marginTop: 0 }}>Почему рекомендаций мало</h3>
            <p className="muted" style={{ margin: 0 }}>
              Подавлено кандидатов: <strong>{suppressedTotal}</strong>. Из них semantic layer убрал{" "}
              <strong>{data.semanticRejectedCandidateCount}</strong>, quality gates и feedback learner
              убрали <strong>{data.qualityFilteredCandidateCount}</strong>.
            </p>
          </section>

          <CounterList title="Причины фильтрации событий" items={data.filterReasons} />
          <CounterList title="Роли устройств" items={data.semanticRoles} />
          <CounterList title="Intent событий" items={data.semanticIntents} />
          <CounterList title="Источник событий" items={data.originTypes} />
          <CounterList title="Behavioral weight buckets" items={data.weightBuckets} />
        </>
      ) : loading ? (
        <section className="card muted">Загрузка диагностики...</section>
      ) : null}
    </div>
  );
}
