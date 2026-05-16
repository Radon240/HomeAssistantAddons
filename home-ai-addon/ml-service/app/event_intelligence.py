from __future__ import annotations

import math
from dataclasses import dataclass
from datetime import datetime, timezone
from enum import StrEnum

from app.device_semantics import DeviceRole, DeviceSemantics, EventIntent
from app.models import EventInput


class OriginType(StrEnum):
    MANUAL = "manual"
    ENVIRONMENT = "environment"
    AUTOMATION = "automation"
    CASCADE = "cascade"
    TELEMETRY = "telemetry"
    SYSTEM = "system"
    UNKNOWN = "unknown"


@dataclass(frozen=True)
class EventIntelligence:
    origin: OriginType
    intent_score: float
    state_importance: float
    event_weight: float
    recency_weight: float
    explanation: str


HIGH_IMPORTANCE_TRANSITIONS = {
    ("off", "on"),
    ("on", "off"),
    ("closed", "open"),
    ("open", "closed"),
    ("locked", "unlocked"),
    ("unlocked", "locked"),
    ("disarmed", "armed_home"),
    ("disarmed", "armed_away"),
    ("armed_home", "disarmed"),
    ("armed_away", "disarmed"),
    ("idle", "playing"),
    ("playing", "idle"),
}

MEDIUM_IMPORTANCE_STATES = {
    "home",
    "not_home",
    "detected",
    "clear",
    "occupied",
    "unoccupied",
    "heat",
    "cool",
    "auto",
}

LOW_IMPORTANCE_DEVICE_CLASSES = {
    "battery",
    "voltage",
    "current",
    "signal_strength",
    "illuminance",
    "humidity",
    "temperature",
    "power",
    "energy",
}


def score_event_intelligence(
    event: EventInput,
    semantics: DeviceSemantics,
    now: datetime | None = None,
    half_life_days: float = 14.0,
) -> EventIntelligence:
    origin = classify_origin(event, semantics)
    intent_score = _intent_score(origin, semantics)
    state_importance = _state_importance(event, semantics)
    recency_weight = _recency_weight(event.time_fired_utc, now, half_life_days)
    origin_multiplier = _origin_multiplier(origin)

    event_weight = round(
        max(0.0, min(1.0, intent_score * state_importance * origin_multiplier * recency_weight)),
        4,
    )

    return EventIntelligence(
        origin=origin,
        intent_score=round(intent_score, 4),
        state_importance=round(state_importance, 4),
        event_weight=event_weight,
        recency_weight=round(recency_weight, 4),
        explanation=(
            f"origin={origin.value}, intent={intent_score:.2f}, "
            f"state_importance={state_importance:.2f}, recency={recency_weight:.2f}"
        ),
    )


def classify_origin(event: EventInput, semantics: DeviceSemantics) -> OriginType:
    if semantics.system_event:
        return OriginType.SYSTEM
    if event.context_parent_id:
        return OriginType.AUTOMATION
    if event.context_user_id:
        return OriginType.MANUAL
    if semantics.intent == EventIntent.ENVIRONMENT_TRIGGER:
        return OriginType.ENVIRONMENT
    if semantics.intent == EventIntent.NOISE:
        return OriginType.TELEMETRY
    if semantics.role == DeviceRole.READ_ONLY:
        return OriginType.TELEMETRY
    if semantics.can_action:
        return OriginType.MANUAL
    return OriginType.UNKNOWN


def _intent_score(origin: OriginType, semantics: DeviceSemantics) -> float:
    if origin == OriginType.MANUAL:
        return 0.95 if semantics.can_action else 0.85
    if origin == OriginType.ENVIRONMENT:
        return 0.65
    if origin == OriginType.AUTOMATION:
        return 0.25
    if origin == OriginType.CASCADE:
        return 0.2
    if origin == OriginType.TELEMETRY:
        return 0.1
    if origin == OriginType.SYSTEM:
        return 0.0
    if semantics.intent == EventIntent.USER_ACTION:
        return 0.75
    if semantics.intent == EventIntent.DEVICE_ACTION:
        return 0.55
    return 0.35


def _origin_multiplier(origin: OriginType) -> float:
    return {
        OriginType.MANUAL: 1.0,
        OriginType.ENVIRONMENT: 0.8,
        OriginType.UNKNOWN: 0.55,
        OriginType.AUTOMATION: 0.25,
        OriginType.CASCADE: 0.2,
        OriginType.TELEMETRY: 0.1,
        OriginType.SYSTEM: 0.0,
    }[origin]


def _state_importance(event: EventInput, semantics: DeviceSemantics) -> float:
    old_state = _norm(event.old_state)
    new_state = _norm(event.new_state)
    if old_state == new_state:
        return 0.0
    if new_state in {"unknown", "unavailable", "none", "null"}:
        return 0.0
    if (old_state, new_state) in HIGH_IMPORTANCE_TRANSITIONS:
        return 1.0
    if new_state in MEDIUM_IMPORTANCE_STATES or old_state in MEDIUM_IMPORTANCE_STATES:
        return 0.75

    numeric_delta = _numeric_delta(old_state, new_state)
    if numeric_delta is not None:
        device_class = (event.device_class or "").strip().lower()
        if device_class in LOW_IMPORTANCE_DEVICE_CLASSES:
            return min(0.55, max(0.05, numeric_delta / 3.0))
        return min(0.85, max(0.15, numeric_delta / 10.0))

    if semantics.role == DeviceRole.READ_ONLY:
        return 0.25
    if semantics.can_action or semantics.can_trigger:
        return 0.7
    return 0.35


def _recency_weight(value: datetime, now: datetime | None, half_life_days: float) -> float:
    ref = now or datetime.now(timezone.utc)
    observed = value if value.tzinfo else value.replace(tzinfo=timezone.utc)
    observed = observed.astimezone(timezone.utc)
    age_days = max(0.0, (ref - observed).total_seconds() / 86400.0)
    half_life = max(1.0, half_life_days)
    return max(0.1, math.exp(-age_days / half_life))


def _numeric_delta(old_state: str, new_state: str) -> float | None:
    try:
        return abs(float(new_state) - float(old_state))
    except (TypeError, ValueError):
        return None


def _norm(value: str | None) -> str:
    return "" if value is None else str(value).strip().lower()
