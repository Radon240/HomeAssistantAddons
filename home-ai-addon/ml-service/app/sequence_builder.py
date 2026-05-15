from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta, timezone

import pandas as pd

from app.models import EventInput

# Protects Raspberry Pi from huge HA bursts inside one time window.
MAX_SESSION_STEPS = 48


@dataclass(frozen=True)
class ActionToken:
    entity_id: str
    new_state: str | None
    friendly_name: str | None
    label: str
    occurred_at: datetime


def _normalize_dt(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


def tokenize_event(event: EventInput) -> ActionToken | None:
    if not event.entity_id:
        return None
    if event.old_state is not None and event.new_state is not None:
        if event.old_state == event.new_state:
            return None

    state = event.new_state or "unknown"
    name = event.friendly_name or event.entity_id.split(".", 1)[-1]
    label = f"{name} → {state}"
    return ActionToken(
        entity_id=event.entity_id,
        new_state=event.new_state,
        friendly_name=event.friendly_name,
        label=label,
        occurred_at=_normalize_dt(event.time_fired_utc),
    )


def build_sessions(
    events: list[EventInput],
    max_gap_seconds: int,
    lookback_hours: int,
) -> list[list[ActionToken]]:
    if not events:
        return []

    cutoff = datetime.now(timezone.utc) - timedelta(hours=lookback_hours)
    tokens: list[ActionToken] = []
    for event in sorted(events, key=lambda e: _normalize_dt(e.time_fired_utc)):
        if _normalize_dt(event.time_fired_utc) < cutoff:
            continue
        token = tokenize_event(event)
        if token is not None:
            tokens.append(token)

    if not tokens:
        return []

    gap = timedelta(seconds=max_gap_seconds)
    sessions: list[list[ActionToken]] = []
    current: list[ActionToken] = [tokens[0]]

    for token in tokens[1:]:
        if token.occurred_at - current[-1].occurred_at <= gap:
            if not current or current[-1].label != token.label:
                current.append(token)
            if len(current) >= MAX_SESSION_STEPS:
                if len(current) >= 2:
                    sessions.append(current)
                current = [token]
        else:
            if len(current) >= 2:
                sessions.append(current)
            current = [token]

    if len(current) >= 2:
        sessions.append(current)

    return sessions


def sessions_to_dataframe(sessions: list[list[ActionToken]]) -> pd.DataFrame:
    rows: list[dict[str, str | int]] = []
    for session_id, session in enumerate(sessions):
        for position, token in enumerate(session):
            rows.append(
                {
                    "session_id": session_id,
                    "position": position,
                    "label": token.label,
                    "entity_id": token.entity_id,
                    "new_state": token.new_state or "",
                    "friendly_name": token.friendly_name or "",
                }
            )
    if not rows:
        return pd.DataFrame(columns=["session_id", "position", "label", "entity_id", "new_state", "friendly_name"])
    return pd.DataFrame(rows)
