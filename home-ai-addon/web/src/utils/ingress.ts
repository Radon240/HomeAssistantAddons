/**
 * Home Assistant Supervisor проксирует UI аддона под путём вида
 *   /api/hassio_ingress/<session-token>/...
 * (одноразовый токен, не slug). На странице запросы вида `fetch("/health")`
 * без префикса попадают в корень HA и дают 404.
 *
 * Префикс фиксируется ОДИН РАЗ на момент загрузки модуля — до того, как
 * React Router начнёт менять `window.location` через pushState.
 */

function computePrefix(pathname: string): string | null {
  const segments = pathname.split("/").filter(Boolean);

  const apiIngress = segments.indexOf("hassio_ingress");
  if (apiIngress > 0 && segments[apiIngress - 1] === "api" && apiIngress + 1 < segments.length) {
    return "/" + segments.slice(0, apiIngress + 2).join("/");
  }

  const legacy = segments.indexOf("ingress");
  if (legacy > 0 && segments[legacy - 1] === "hassio" && legacy + 1 < segments.length) {
    return "/" + segments.slice(0, legacy + 2).join("/");
  }

  return null;
}

const initialPathname = typeof window !== "undefined" ? window.location.pathname : "";
const cachedIngressPrefix = computePrefix(initialPathname);

export function tryGetHaIngressPrefix(): string | null {
  return cachedIngressPrefix;
}

/**
 * Возвращает URL к backend (тот же Kestrel) с учётом Ingress.
 * Если страница открыта НЕ через Ingress (например dev-сервер Vite или прямой http),
 * префикса нет и путь возвращается как есть — прокси Vite/Kestrel сам всё разрулит.
 */
export function resolveBackendUrl(path: string): string {
  const normalized = path.startsWith("/") ? path : `/${path}`;
  const prefix = tryGetHaIngressPrefix();
  return prefix ? `${prefix}${normalized}` : normalized;
}
