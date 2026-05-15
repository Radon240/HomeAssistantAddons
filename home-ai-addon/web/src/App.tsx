import { NavLink, Route, Routes } from "react-router-dom";
import { DashboardPage } from "./pages/DashboardPage";
import { EventsPage } from "./pages/EventsPage";
import { RecommendationsPage } from "./pages/RecommendationsPage";
import { StatusPage } from "./pages/StatusPage";

export function App() {
  return (
    <div className="layout">
      <header>
        <div className="brand">Home AI Addon</div>
        <nav>
          <NavLink to="/" end>
            Дашборд
          </NavLink>
          <NavLink to="/dashboard">Обзор</NavLink>
          <NavLink to="/events">События</NavLink>
          <NavLink to="/recommendations">Рекомендации</NavLink>
          <NavLink to="/status">Статус</NavLink>
        </nav>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/events" element={<EventsPage />} />
          <Route path="/recommendations" element={<RecommendationsPage />} />
          <Route path="/status" element={<StatusPage />} />
        </Routes>
      </main>
    </div>
  );
}
