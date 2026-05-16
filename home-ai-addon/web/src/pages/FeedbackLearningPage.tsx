import { FeedbackLearningPanel } from "../components/FeedbackLearningPanel";

export function FeedbackLearningPage() {
  return (
    <div className="row">
      <section className="card">
        <h2 style={{ marginTop: 0 }}>Обучение</h2>
        <p className="muted" style={{ margin: 0 }}>
          Управление дообучением на отзывах: полный сброс, очистка плохих примеров и возврат
          скрытых рекомендаций.
        </p>
      </section>
      <FeedbackLearningPanel />
    </div>
  );
}
