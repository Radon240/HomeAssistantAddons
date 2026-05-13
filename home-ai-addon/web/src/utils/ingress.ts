/**
 * Home Assistant Ingress открывает UI по пути вида
 * /hassio/ingress/<slug>/... (см. документацию Supervisor).
 * Запросы на "/health" уходят на корень домена HA и дают 404 — нужен префикс Ingress.
 */
export function tryGetHaIngressPrefix(): string | null {
  if (typeof window === "undefined") {
    return null;
  }

  const segments = window.location.pathname.split("/").filter(Boolean);
  const ingressIndex = segments.indexOf("ingress");
  if (ingressIndex < 1 || segments[ingressIndex - 1] !== "hassio") {
    return null;
  }

  if (ingressIndex + 1 >= segments.length) {
    return null;
  }

  return "/" + segments.slice(0, ingressIndex + 2).join("/");
}

/** URL к backend (тот же Kestrel), с учётом Ingress. В dev — путь как есть (прокси Vite). */
export function resolveBackendUrl(path: string): string {
  const normalized = path.startsWith("/") ? path : `/${path}`;
  if (import.meta.env.DEV) {
    return normalized;
  }

  const prefix = tryGetHaIngressPrefix();
  if (prefix) {
    return `${prefix}${normalized}`;
  }

  return normalized;
}
