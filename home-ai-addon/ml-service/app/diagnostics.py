from __future__ import annotations

from collections import Counter
from datetime import datetime, timedelta, timezone

from app.device_semantics import DeviceRole, classify_event, is_meaningful_automation
from app.models import AnalyzeOptions, DiagnosticsCounter, DiagnosticsResponse, EventInput
from app.pattern_miner import _fits_step_gaps, _step_gaps, mine_patterns
from app.recommender import analyze_events
from app.river_tracker import OnlinePatternTracker
from app.sequence_builder import ActionToken, build_sessions


def analyze_diagnostics(events: list[EventInput], options: AnalyzeOptions) -> DiagnosticsResponse:
    filtered: Counter[str] = Counter()
    roles: Counter[str] = Counter()
    intents: Counter[str] = Counter()

    cutoff = datetime.now(timezone.utc) - timedelta(hours=options.lookback_hours)
    considered = 0
    eligible = 0

    for event in events:
        occurred_at = _normalize_dt(event.time_fired_utc)
        if occurred_at < cutoff:
            filtered["outside_lookback"] += 1
            continue

        considered += 1
        semantics = classify_event(event)
        roles[semantics.role.value] += 1
        intents[semantics.intent.value] += 1

        if semantics.system_event:
            filtered["system_or_unavailable"] += 1
            continue
        if semantics.noisy:
            filtered["noisy_diagnostic"] += 1
            continue
        if not semantics.significant:
            filtered["insignificant_change"] += 1
            continue
        if not semantics.can_trigger and not semantics.can_action:
            filtered["context_only"] += 1
            continue
        eligible += 1

    sessions = build_sessions(
        events,
        max_gap_seconds=options.max_gap_seconds,
        lookback_hours=options.lookback_hours,
    )
    sequence_stats = _sequence_diagnostics(sessions, options)

    mined = mine_patterns(
        sessions,
        min_support=options.min_support,
        max_sequence_length=options.max_sequence_length,
        max_step_gap_seconds=options.max_step_gap_seconds,
        tracker=OnlinePatternTracker(),
    )
    recommendations = analyze_events(events, options).recommendations
    quality_filtered = max(0, len(mined) - len(recommendations))

    return DiagnosticsResponse(
        analyzed_event_count=considered,
        eligible_event_count=eligible,
        session_count=len(sessions),
        raw_sequence_candidate_count=sequence_stats["raw"],
        semantic_rejected_candidate_count=sequence_stats["semantic_rejected"],
        sensor_to_sensor_candidate_count=sequence_stats["sensor_to_sensor"],
        meaningful_candidate_count=sequence_stats["meaningful"],
        quality_filtered_candidate_count=quality_filtered,
        recommendation_count=len(recommendations),
        filter_reasons=_to_counters(filtered),
        semantic_roles=_to_counters(roles),
        semantic_intents=_to_counters(intents),
        options_used=options.model_dump(by_alias=True),
    )


def _sequence_diagnostics(
    sessions: list[list[ActionToken]],
    options: AnalyzeOptions,
) -> dict[str, int]:
    raw = 0
    semantic_rejected = 0
    sensor_to_sensor = 0
    meaningful = 0

    for session in sessions:
        for length in range(2, min(options.max_sequence_length, len(session)) + 1):
            for start in range(0, len(session) - length + 1):
                tokens = tuple(session[start : start + length])
                gaps = _step_gaps(tokens)
                if not _fits_step_gaps(gaps, options.max_step_gap_seconds):
                    continue

                raw += 1
                semantics = [token.semantics for token in tokens]
                ok, _ = is_meaningful_automation(semantics)
                if ok:
                    meaningful += 1
                    continue

                semantic_rejected += 1
                if all(item.role in {DeviceRole.SENSOR, DeviceRole.READ_ONLY} for item in semantics):
                    sensor_to_sensor += 1

    return {
        "raw": raw,
        "semantic_rejected": semantic_rejected,
        "sensor_to_sensor": sensor_to_sensor,
        "meaningful": meaningful,
    }


def _normalize_dt(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


def _to_counters(counter: Counter[str]) -> list[DiagnosticsCounter]:
    return [
        DiagnosticsCounter(key=key, count=count)
        for key, count in sorted(counter.items(), key=lambda item: (-item[1], item[0]))
    ]
