import { Link } from "react-router-dom";
import { EventsHourlyChart } from "../components/EventsHourlyChart";
import { HomeAssistantStatusCard } from "../components/HomeAssistantStatusCard";

export function DashboardPage() {
  return (
    <div className="row">
      <section className="card">
        <h2 style={{ marginTop: 0 }}>Дашборд</h2>
        <p className="muted">
          Home AI Addon — мониторинг Home Assistant, сохранение событий в SQLite и UI через Ingress.
        </p>
        <ul>
          <li>
            <Link to="/events">События</Link> — лента и фильтр по entity_id.
          </li>
          <li>
            <Link to="/status">Статус</Link> — health, API, метрики.
          </li>
        </ul>
      </section>
      <HomeAssistantStatusCard />
      <EventsHourlyChart hours={1} />
    </div>
  );
}
