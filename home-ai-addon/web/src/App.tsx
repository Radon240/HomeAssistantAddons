import { Navigate, NavLink, Route, Routes } from "react-router-dom";
import { DashboardPage } from "./pages/DashboardPage";
import { EventsPage } from "./pages/EventsPage";
import { AnomaliesPage } from "./pages/AnomaliesPage";
import { FeedbackLearningPage } from "./pages/FeedbackLearningPage";
import { MlDiagnosticsPage } from "./pages/MlDiagnosticsPage";
import { RecommendationsPage } from "./pages/RecommendationsPage";
import { SemanticOverridesPage } from "./pages/SemanticOverridesPage";
import { StatusPage } from "./pages/StatusPage";

export function App() {
  return (
    <div className="layout">
      <header>
        <div className="brand">Home AI Addon</div>
        <nav>
          <NavLink to="/" end>
            Обзор
          </NavLink>
          <NavLink to="/events">События</NavLink>
          <NavLink to="/recommendations">Рекомендации</NavLink>
          <NavLink to="/feedback">Обучение</NavLink>
          <NavLink to="/diagnostics">Диагностика ML</NavLink>
          <NavLink to="/semantic-overrides">Semantic Overrides</NavLink>
          <NavLink to="/anomalies">Аномалии</NavLink>
          <NavLink to="/status">Статус</NavLink>
        </nav>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/dashboard" element={<Navigate to="/" replace />} />
          <Route path="/events" element={<EventsPage />} />
          <Route path="/recommendations" element={<RecommendationsPage />} />
          <Route path="/feedback" element={<FeedbackLearningPage />} />
          <Route path="/diagnostics" element={<MlDiagnosticsPage />} />
          <Route path="/semantic-overrides" element={<SemanticOverridesPage />} />
          <Route path="/anomalies" element={<AnomaliesPage />} />
          <Route path="/status" element={<StatusPage />} />
        </Routes>
      </main>
    </div>
  );
}
