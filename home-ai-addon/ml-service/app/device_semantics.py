from __future__ import annotations

import json
import os
from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path
from typing import Any

from app.models import EventInput


class DeviceRole(StrEnum):
    SENSOR = "sensor"
    ACTUATOR = "actuator"
    HYBRID = "hybrid"
    READ_ONLY = "read_only"


class EventIntent(StrEnum):
    USER_ACTION = "user_action"
    ENVIRONMENT_TRIGGER = "environment_trigger"
    DEVICE_ACTION = "device_action"
    CONTEXT = "context"
    SYSTEM = "system"
    NOISE = "noise"


READ_ONLY_DOMAINS = {
    "sensor",
    "sun",
    "weather",
    "zone",
    "update",
    "calendar",
}

CONTEXT_DOMAINS = {
    "person",
    "device_tracker",
    "input_select",
    "input_datetime",
    "input_number",
    "input_text",
}

TRIGGER_ONLY_DOMAINS = {
    "binary_sensor",
    "event",
    "button",
    "input_button",
}

ACTUATOR_DOMAINS = {
    "light",
    "switch",
    "cover",
    "climate",
    "fan",
    "lock",
    "media_player",
    "vacuum",
    "humidifier",
    "water_heater",
    "lawn_mower",
    "siren",
    "number",
    "select",
    "scene",
    "script",
    "automation",
}

HYBRID_DOMAINS = {
    "alarm_control_panel",
    "remote",
    "input_boolean",
}

MEANINGFUL_BINARY_HINTS = {
    "door",
    "window",
    "motion",
    "occupancy",
    "presence",
    "opening",
    "garage",
    "lock",
    "smoke",
    "gas",
    "moisture",
    "water",
    "vibration",
    "problem",
    "safety",
}

NOISY_ENTITY_HINTS = {
    "rssi",
    "linkquality",
    "link_quality",
    "signal",
    "uptime",
    "last_seen",
    "last_updated",
    "timestamp",
    "heartbeat",
    "voltage",
    "power_factor",
    "diagnostic",
    "wifi",
    "ssid",
    "ip_address",
    "temperature_offset",
    "illuminance_lux",
}

SYSTEM_STATE_VALUES = {
    "unknown",
    "unavailable",
    "none",
    "null",
    "restored",
}

PASSIVE_SENSOR_HINTS = {
    "temperature",
    "humidity",
    "pressure",
    "illuminance",
    "battery",
    "energy",
    "power",
    "voltage",
    "current",
    "rssi",
    "linkquality",
}

ACTION_DEVICE_CLASSES = {
    "outlet",
    "switch",
    "light",
    "garage",
    "door",
    "window",
    "shade",
    "curtain",
    "lock",
    "temperature",
    "heat",
    "cool",
}

OVERRIDES_PATH = Path(os.environ.get("SEMANTIC_OVERRIDES_PATH", "/data/semantic-overrides.json"))
_OVERRIDES_MTIME: float | None = None
_OVERRIDES_CACHE: dict[str, dict[str, Any]] = {}


@dataclass(frozen=True)
class DeviceSemantics:
    entity_id: str
    domain: str
    role: DeviceRole
    intent: EventIntent
    can_trigger: bool
    can_action: bool
    noisy: bool
    system_event: bool
    significant: bool
    reason: str


def classify_event(event: EventInput) -> DeviceSemantics:
    entity_id = event.entity_id.strip()
    domain = (event.domain or "").strip() or (
        entity_id.split(".", 1)[0] if "." in entity_id else entity_id
    )
    text = _semantic_text(entity_id, event.friendly_name)
    device_class = (event.device_class or "").strip().lower()
    unit = (event.unit_of_measurement or "").strip().lower()
    entity_category = (event.entity_category or "").strip().lower()
    new_state = _norm_state(event.new_state)
    old_state = _norm_state(event.old_state)

    system_event = _is_system_state(new_state) or _is_system_state(old_state)
    noisy = _is_noisy_entity(domain, text, device_class, entity_category)
    role = _classify_role(domain, text, device_class, entity_category)
    significant = _is_significant_change(old_state, new_state, domain, text, device_class, unit)

    can_trigger = _can_trigger(domain, role, text, new_state, system_event, noisy, significant)
    can_action = _can_action(domain, role, new_state, system_event, noisy, significant)
    intent = _classify_intent(domain, role, can_trigger, can_action, noisy, system_event)
    reason = _build_reason(role, intent, can_trigger, can_action, noisy, system_event, significant)

    semantics = DeviceSemantics(
        entity_id=entity_id,
        domain=domain,
        role=role,
        intent=intent,
        can_trigger=can_trigger,
        can_action=can_action,
        noisy=noisy,
        system_event=system_event,
        significant=significant,
        reason=reason,
    )
    return _apply_override(semantics)


def is_meaningful_automation(semantics: list[DeviceSemantics]) -> tuple[bool, str]:
    if len(semantics) < 2:
        return False, "Нужно минимум два шага."

    trigger = semantics[0]
    actions = semantics[1:]

    if not trigger.can_trigger:
        return False, f"Первый шаг {trigger.entity_id} не является осмысленным trigger."

    if any(not action.can_action for action in actions):
        blocked = [action.entity_id for action in actions if not action.can_action]
        return False, "Действия должны быть управляемыми устройствами: " + ", ".join(blocked)

    if trigger.role == DeviceRole.READ_ONLY and all(
        action.role in {DeviceRole.READ_ONLY, DeviceRole.SENSOR} for action in actions
    ):
        return False, "Пассивные сенсоры не создают automation opportunity."

    if all(item.role in {DeviceRole.SENSOR, DeviceRole.READ_ONLY} for item in semantics):
        return False, "Sensor -> sensor correlation не является автоматизацией."

    if len({item.entity_id for item in semantics}) == 1:
        return False, "Паттерн меняет только одну сущность."

    return True, _semantic_summary(trigger, actions)


def should_ignore_for_anomaly(event: EventInput) -> tuple[bool, str]:
    semantics = classify_event(event)
    if semantics.system_event:
        return True, "system/unavailable state"
    if semantics.noisy:
        return True, "noisy diagnostic sensor"
    if not semantics.significant:
        return True, "insignificant change"
    if semantics.role == DeviceRole.READ_ONLY and semantics.domain in {"sun", "weather", "zone"}:
        return True, "context-only entity"
    return False, semantics.reason


def _classify_role(domain: str, text: str, device_class: str, entity_category: str) -> DeviceRole:
    if entity_category in {"diagnostic", "config"}:
        return DeviceRole.READ_ONLY
    if domain in ACTUATOR_DOMAINS:
        return DeviceRole.ACTUATOR
    if domain in HYBRID_DOMAINS:
        return DeviceRole.HYBRID
    if domain in TRIGGER_ONLY_DOMAINS:
        return DeviceRole.SENSOR
    if domain in CONTEXT_DOMAINS or domain in READ_ONLY_DOMAINS:
        return DeviceRole.READ_ONLY
    if device_class in MEANINGFUL_BINARY_HINTS or any(hint in text for hint in MEANINGFUL_BINARY_HINTS):
        return DeviceRole.SENSOR
    return DeviceRole.READ_ONLY


def _apply_override(semantics: DeviceSemantics) -> DeviceSemantics:
    override = _load_overrides().get(semantics.entity_id)
    if not override:
        return semantics

    role = _enum_override(DeviceRole, override.get("role"), semantics.role)
    intent = _enum_override(EventIntent, override.get("intent"), semantics.intent)
    can_trigger = _bool_override(override.get("canTrigger"), semantics.can_trigger)
    can_action = _bool_override(override.get("canAction"), semantics.can_action)
    noisy = _bool_override(override.get("noisy"), semantics.noisy)
    system_event = _bool_override(override.get("systemEvent"), semantics.system_event)
    significant = _bool_override(override.get("significant"), semantics.significant)
    reason = str(override.get("reason") or f"semantic override: {semantics.reason}")

    return DeviceSemantics(
        entity_id=semantics.entity_id,
        domain=semantics.domain,
        role=role,
        intent=intent,
        can_trigger=can_trigger,
        can_action=can_action,
        noisy=noisy,
        system_event=system_event,
        significant=significant,
        reason=reason,
    )


def _load_overrides() -> dict[str, dict[str, Any]]:
    global _OVERRIDES_MTIME, _OVERRIDES_CACHE

    try:
        stat = OVERRIDES_PATH.stat()
    except FileNotFoundError:
        _OVERRIDES_MTIME = None
        _OVERRIDES_CACHE = {}
        return _OVERRIDES_CACHE
    except OSError:
        return _OVERRIDES_CACHE

    if _OVERRIDES_MTIME == stat.st_mtime:
        return _OVERRIDES_CACHE

    try:
        payload = json.loads(OVERRIDES_PATH.read_text(encoding="utf-8"))
        entities = payload.get("entities", {}) if isinstance(payload, dict) else {}
        _OVERRIDES_CACHE = {
            str(entity_id): override
            for entity_id, override in entities.items()
            if isinstance(override, dict)
        }
        _OVERRIDES_MTIME = stat.st_mtime
    except (OSError, json.JSONDecodeError):
        _OVERRIDES_CACHE = {}
        _OVERRIDES_MTIME = stat.st_mtime

    return _OVERRIDES_CACHE


def _enum_override(enum_type: type[StrEnum], raw: Any, current: Any) -> Any:
    if raw is None:
        return current
    try:
        return enum_type(str(raw))
    except ValueError:
        return current


def _bool_override(raw: Any, current: bool) -> bool:
    return raw if isinstance(raw, bool) else current


def _classify_intent(
    domain: str,
    role: DeviceRole,
    can_trigger: bool,
    can_action: bool,
    noisy: bool,
    system_event: bool,
) -> EventIntent:
    if system_event:
        return EventIntent.SYSTEM
    if noisy:
        return EventIntent.NOISE
    if can_action and domain in ACTUATOR_DOMAINS | HYBRID_DOMAINS:
        return EventIntent.USER_ACTION
    if can_trigger and role == DeviceRole.SENSOR:
        return EventIntent.ENVIRONMENT_TRIGGER
    if can_action:
        return EventIntent.DEVICE_ACTION
    return EventIntent.CONTEXT


def _can_trigger(
    domain: str,
    role: DeviceRole,
    text: str,
    state: str,
    system_event: bool,
    noisy: bool,
    significant: bool,
) -> bool:
    if system_event or noisy or not significant:
        return False
    if domain in ACTUATOR_DOMAINS | HYBRID_DOMAINS:
        return state not in SYSTEM_STATE_VALUES
    if domain in TRIGGER_ONLY_DOMAINS:
        return domain != "binary_sensor" or any(hint in text for hint in MEANINGFUL_BINARY_HINTS)
    if domain in {"person", "device_tracker"}:
        return True
    return role == DeviceRole.SENSOR and any(hint in text for hint in MEANINGFUL_BINARY_HINTS)


def _can_action(
    domain: str,
    role: DeviceRole,
    state: str,
    system_event: bool,
    noisy: bool,
    significant: bool,
) -> bool:
    if system_event or noisy or not significant:
        return False
    if role not in {DeviceRole.ACTUATOR, DeviceRole.HYBRID}:
        return False
    if domain in {"automation"}:
        return False
    return state not in SYSTEM_STATE_VALUES


def _is_system_state(value: str) -> bool:
    return value in SYSTEM_STATE_VALUES


def _is_noisy_entity(domain: str, text: str, device_class: str, entity_category: str) -> bool:
    if entity_category == "diagnostic":
        return True
    if domain == "sensor" and (
        device_class in NOISY_ENTITY_HINTS or any(hint in text for hint in NOISY_ENTITY_HINTS)
    ):
        return True
    return domain in {"update"}


def _is_significant_change(
    old_state: str,
    new_state: str,
    domain: str,
    text: str,
    device_class: str,
    unit: str,
) -> bool:
    if old_state == new_state:
        return False
    if _is_system_state(new_state):
        return False
    if domain in {"sensor", "number"}:
        old_num = _try_float(old_state)
        new_num = _try_float(new_state)
        if old_num is not None and new_num is not None:
            delta = abs(new_num - old_num)
            baseline = max(abs(old_num), abs(new_num), 1.0)
            relative = delta / baseline
            if device_class == "battery" or "battery" in text or unit == "%":
                return delta >= 2.0
            if device_class in {"temperature", "humidity", "illuminance"} or any(
                hint in text for hint in {"temperature", "humidity", "illuminance"}
            ):
                return delta >= 1.0 and relative >= 0.01
            return delta >= 0.5 and relative >= 0.02
    return True


def _build_reason(
    role: DeviceRole,
    intent: EventIntent,
    can_trigger: bool,
    can_action: bool,
    noisy: bool,
    system_event: bool,
    significant: bool,
) -> str:
    if system_event:
        return "system event"
    if noisy:
        return "diagnostic/noisy sensor"
    if not significant:
        return "insignificant change"
    capabilities = []
    if can_trigger:
        capabilities.append("trigger")
    if can_action:
        capabilities.append("action")
    capability_text = ", ".join(capabilities) if capabilities else "context only"
    return f"{role.value}, {intent.value}, {capability_text}"


def _semantic_summary(trigger: DeviceSemantics, actions: list[DeviceSemantics]) -> str:
    action_entities = ", ".join(action.entity_id for action in actions)
    return (
        f"{trigger.entity_id} classified as {trigger.role.value}/{trigger.intent.value}; "
        f"actions are controllable devices: {action_entities}."
    )


def _semantic_text(entity_id: str, friendly_name: str | None) -> str:
    return f"{entity_id} {friendly_name or ''}".lower().replace("-", "_").replace(" ", "_")


def _norm_state(value: str | None) -> str:
    if value is None:
        return ""
    return str(value).strip().lower()


def _try_float(value: str) -> float | None:
    try:
        return float(value)
    except (TypeError, ValueError):
        return None
