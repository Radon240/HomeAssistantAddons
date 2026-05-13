import { Link } from "react-router-dom";
import { HomeAssistantStatusCard } from "../components/HomeAssistantStatusCard";

export function DashboardPage() {
  return (
    <div className="row">
      <section className="card">
        <h2 style={{ marginTop: 0 }}>Дашборд</h2>
        <p className="muted">
          Home AI Addon — интерфейс Home Assistant Add-on. API и health обслуживаются тем же
          Kestrel-процессом, что и статика Vite-сборки.
        </p>
        <ul>
          <li>
            <Link to="/status">Страница статуса</Link> — проверка{" "}
            <span className="mono">/health</span>, <span className="mono">/api/info</span> и{" "}
            <span className="mono">/api/ping</span>.
          </li>
        </ul>
      </section>
      <HomeAssistantStatusCard />
    </div>
  );
}
