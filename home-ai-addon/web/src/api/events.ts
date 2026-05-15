import { fetchJson } from "./client";

export type StateChangeEventDto = {
  id: number;
  entityId: string;
  oldState: string | null;
  newState: string | null;
  friendlyName: string | null;
  timeFiredUtc: string;
  receivedAtUtc: string;
};

export type EventsListResponse = {
  count: number;
  items: StateChangeEventDto[];
};

export type HourlyEventBucket = {
  hourUtc: string;
  count: number;
};

export type HourlyStatsResponse = {
  hours: number;
  buckets: HourlyEventBucket[];
};

export type MetricsResponse = {
  webSocketConnected: boolean;
  eventsReceivedInMemory: number;
  eventsPersisted: number;
  eventsFiltered: number;
  reconnectCount: number;
  lastEventReceivedAtUtc: string | null;
  lastEventAgeSeconds: number | null;
  lastError: string | null;
};

export type HomeAssistantEntityDto = {
  entityId: string;
  domain: string;
  state: string | null;
  friendlyName: string | null;
};

export type EntitiesListResponse = {
  count: number;
  items: HomeAssistantEntityDto[];
};

export function fetchEvents(limit = 100, entity?: string): Promise<EventsListResponse> {
  const params = new URLSearchParams({ limit: String(limit) });
  if (entity?.trim()) {
    params.set("entity", entity.trim());
  }
  return fetchJson<EventsListResponse>(`/api/events?${params}`);
}

export function fetchHourlyStats(hours = 1): Promise<HourlyStatsResponse> {
  return fetchJson<HourlyStatsResponse>(`/api/events/stats/hourly?hours=${hours}`);
}

export function fetchMetrics(): Promise<MetricsResponse> {
  return fetchJson<MetricsResponse>("/api/metrics");
}

export function fetchEntities(): Promise<EntitiesListResponse> {
  return fetchJson<EntitiesListResponse>("/api/homeassistant/entities");
}
