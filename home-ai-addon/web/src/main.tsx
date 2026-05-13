import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "./App";
import { tryGetHaIngressPrefix } from "./utils/ingress";
import "./styles.css";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("Не найден элемент #root");
}

const ingressBasename = tryGetHaIngressPrefix() ?? undefined;

createRoot(rootElement).render(
  <StrictMode>
    <BrowserRouter basename={ingressBasename}>
      <App />
    </BrowserRouter>
  </StrictMode>
);
