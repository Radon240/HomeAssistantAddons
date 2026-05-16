from __future__ import annotations

import hashlib
import logging
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any

import numpy as np
import pandas as pd
from sklearn.ensemble import IsolationForest

from app.device_semantics import classify_event, should_ignore_for_anomaly
from app.models import AnomalyDetectionOptions, AnomalyItem, AnomalyResponse, EventInput

logger = logging.getLogger(__name__)

SEVERITY_LOW = "low"
SEVERITY_MEDIUM = "medium"
SEVERITY_HIGH = "high"

TYPE_ACTIVITY_SPIKE = "activity_spike"
TYPE_UNUSUAL_TIME = "unusual_time"
TYPE_CONSUMPTION_SPIKE = "consumption_spike"
TYPE_DEVICE_BEHAVIOR = "device_behavior"

METHOD_Z_SCORE = "z_score"
METHOD_ROLLING_AVERAGE = "rolling_average"
METHOD_ISOLATION_FOREST = "isolation_forest"
METHOD_HOUR_HISTOGRAM = "hour_histogram"


@dataclass(frozen=True)
class _RawAnomaly:
    entity_id: str
    anomaly_type: str
    method: str
    score: float
    title: str
    explanation: str
    detected_at: datetime
    related_event_ids: list[int]
    metrics: dict[str, Any]


def detect_anomalies(
    events: list[EventInput],
    options: AnomalyDetectionOptions,
) -> AnomalyResponse:
    if len(events) < options.min_events:
        return AnomalyResponse(
            analyzed_event_count=len(events),
            anomaly_count=0,
            anomalies=[],
            options_used=options.model_dump(by_alias=True),
        )

    frame = _events_to_frame(events)
    if frame.empty or len(frame) < options.min_events:
        return AnomalyResponse(
            analyzed_event_count=len(events),
            anomaly_count=0,
            anomalies=[],
            options_used={
                **options.model_dump(by_alias=True),
                "filteredEventCount": int(len(frame)),
            },
        )

    candidates: list[_RawAnomaly] = []
    candidates.extend(_detect_hourly_zscore(frame, options))
    candidates.extend(_detect_unusual_hours(frame, options))
    candidates.extend(_detect_rolling_frequency(frame, options))
    candidates.extend(_detect_numeric_spikes(frame, options))
    candidates.extend(_detect_isolation_forest(frame, options))

    merged = _merge_candidates(candidates, options)
    anomalies = [_to_item(item, options) for item in merged]
    anomalies.sort(key=lambda item: item.score, reverse=True)

    return AnomalyResponse(
        analyzed_event_count=len(events),
        anomaly_count=len(anomalies),
        anomalies=anomalies[: options.max_results],
        options_used={
            **options.model_dump(by_alias=True),
            "filteredEventCount": int(len(frame)),
        },
    )


def _events_to_frame(events: list[EventInput]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for event in events:
        ignore, ignore_reason = should_ignore_for_anomaly(event)
        if ignore:
            logger.debug("Ignoring anomaly event %s: %s", event.entity_id, ignore_reason)
            continue
        semantics = classify_event(event)
        rows.append(
            {
                "id": event.id,
                "entity_id": event.entity_id,
                "old_state": event.old_state,
                "new_state": event.new_state,
                "friendly_name": event.friendly_name,
                "time_fired_utc": event.time_fired_utc,
                "received_at_utc": event.received_at_utc,
                "role": semantics.role.value,
                "intent": semantics.intent.value,
                "semantic_reason": semantics.reason,
                "can_trigger": semantics.can_trigger,
                "can_action": semantics.can_action,
            }
        )
    frame = pd.DataFrame(rows)
    if frame.empty:
        return frame
    frame["time_fired_utc"] = pd.to_datetime(frame["time_fired_utc"], utc=True)
    frame["received_at_utc"] = pd.to_datetime(frame["received_at_utc"], utc=True)
    frame["hour"] = frame["time_fired_utc"].dt.hour
    frame["dow"] = frame["time_fired_utc"].dt.dayofweek
    frame["hour_bucket"] = frame["time_fired_utc"].dt.floor("h")
    frame["domain"] = frame["entity_id"].str.split(".").str[0]
    frame["label"] = frame["friendly_name"].fillna(frame["entity_id"])
    return frame.sort_values("time_fired_utc")


def _detect_hourly_zscore(
    frame: pd.DataFrame,
    options: AnomalyDetectionOptions,
) -> list[_RawAnomaly]:
    results: list[_RawAnomaly] = []
    frame = _behavior_frame(frame)
    if frame.empty:
        return results
    grouped = (
        frame.groupby(["entity_id", "hour_bucket"], as_index=False)
        .agg(
            count=("id", "size"),
            ids=("id", list),
            label=("label", "first"),
            semantic_reason=("semantic_reason", "first"),
        )
        .sort_values("hour_bucket")
    )
    if grouped.empty:
        return results

    for entity_id, entity_rows in grouped.groupby("entity_id"):
        counts = entity_rows["count"].astype(float)
        if len(counts) < options.min_hourly_samples:
            continue

        rolling_mean = counts.rolling(
            window=options.rolling_window_hours,
            min_periods=options.min_hourly_samples,
        ).mean()
        rolling_std = counts.rolling(
            window=options.rolling_window_hours,
            min_periods=options.min_hourly_samples,
        ).std(ddof=0)
        z_scores = (counts - rolling_mean) / rolling_std.replace(0, np.nan)
        z_scores = z_scores.fillna(0.0)

        for idx, z_value in enumerate(z_scores):
            if z_value < options.z_score_threshold:
                continue
            row = entity_rows.iloc[idx]
            score = _zscore_to_score(float(z_value), options.z_score_threshold)
            results.append(
                _RawAnomaly(
                    entity_id=str(entity_id),
                    anomaly_type=TYPE_ACTIVITY_SPIKE,
                    method=METHOD_Z_SCORE,
                    score=score,
                    title=f"Резкий рост активности: {row['label']}",
                    explanation=(
                        f"За час {row['hour_bucket'].strftime('%d.%m %H:00')} UTC зафиксировано "
                        f"{int(row['count'])} событий при типичных "
                        f"{rolling_mean.iloc[idx]:.1f}±{rolling_std.iloc[idx]:.1f}. "
                        f"Z-score={z_value:.2f} (порог {options.z_score_threshold:.1f})."
                    ),
                    detected_at=row["hour_bucket"].to_pydatetime().replace(tzinfo=UTC),
                    related_event_ids=[int(v) for v in row["ids"][:20]],
                    metrics={
                        "reason": "activity exceeded historical hourly baseline",
                        "semanticReason": str(row.get("semantic_reason", "")),
                        "zScore": round(float(z_value), 3),
                        "hourlyCount": int(row["count"]),
                        "rollingMean": round(float(rolling_mean.iloc[idx]), 3),
                        "rollingStd": round(float(rolling_std.iloc[idx]), 3),
                    },
                )
            )
    return results


def _detect_unusual_hours(
    frame: pd.DataFrame,
    options: AnomalyDetectionOptions,
) -> list[_RawAnomaly]:
    results: list[_RawAnomaly] = []
    frame = _behavior_frame(frame)
    if frame.empty:
        return results
    for entity_id, entity_rows in frame.groupby("entity_id"):
        if len(entity_rows) < options.min_events_per_entity:
            continue

        hour_counts = entity_rows["hour"].value_counts(normalize=True)
        if hour_counts.empty:
            continue

        recent = entity_rows.tail(max(3, options.min_events_per_entity // 3))
        for _, row in recent.iterrows():
            hour = int(row["hour"])
            probability = float(hour_counts.get(hour, 0.0))
            if probability >= options.unusual_hour_max_ratio:
                continue

            score = _ratio_to_score(probability, options.unusual_hour_max_ratio)
            if score < options.min_score:
                continue

            typical_hours = hour_counts.nlargest(3).index.tolist()
            results.append(
                _RawAnomaly(
                    entity_id=str(entity_id),
                    anomaly_type=TYPE_UNUSUAL_TIME,
                    method=METHOD_HOUR_HISTOGRAM,
                    score=score,
                    title=f"Активность в нетипичное время: {row['label']}",
                    explanation=(
                        f"Событие в {hour:02d}:00 UTC встречается редко "
                        f"({probability * 100:.1f}% истории). "
                        f"Обычные часы: {', '.join(f'{h:02d}:00' for h in typical_hours)}."
                    ),
                    detected_at=row["time_fired_utc"].to_pydatetime(),
                    related_event_ids=[int(row["id"])],
                    metrics={
                        "reason": "event occurred outside historical time context",
                        "semanticReason": str(row.get("semantic_reason", "")),
                        "hour": hour,
                        "hourProbability": round(probability, 4),
                        "typicalHours": [int(h) for h in typical_hours],
                    },
                )
            )
    return results


def _detect_rolling_frequency(
    frame: pd.DataFrame,
    options: AnomalyDetectionOptions,
) -> list[_RawAnomaly]:
    results: list[_RawAnomaly] = []
    frame = _behavior_frame(frame)
    if frame.empty:
        return results
    window = f"{options.rolling_window_hours}h"

    for entity_id, entity_rows in frame.groupby("entity_id"):
        if len(entity_rows) < options.min_events_per_entity:
            continue

        indexed = entity_rows.set_index("time_fired_utc").sort_index()
        counts = (
            indexed["id"]
            .resample(window)
            .count()
            .astype(float)
        )
        if len(counts) < options.min_hourly_samples:
            continue

        rolling_mean = counts.rolling(
            window=options.rolling_window_hours,
            min_periods=options.min_hourly_samples,
        ).mean()
        rolling_std = counts.rolling(
            window=options.rolling_window_hours,
            min_periods=options.min_hourly_samples,
        ).std(ddof=0)

        latest_ts = counts.index[-1]
        latest_count = float(counts.iloc[-1])
        mean_value = float(rolling_mean.iloc[-1])
        std_value = float(rolling_std.iloc[-1]) if not np.isnan(rolling_std.iloc[-1]) else 0.0
        if std_value <= 0:
            continue

        z_value = (latest_count - mean_value) / std_value
        if z_value < options.z_score_threshold:
            continue

        score = _zscore_to_score(z_value, options.z_score_threshold)
        label = str(entity_rows["label"].iloc[-1])
        bucket_events = entity_rows[entity_rows["hour_bucket"] == latest_ts.floor("h")]
        event_ids = bucket_events["id"].astype(int).tolist()[:20]
        results.append(
            _RawAnomaly(
                entity_id=str(entity_id),
                anomaly_type=TYPE_DEVICE_BEHAVIOR,
                method=METHOD_ROLLING_AVERAGE,
                score=score,
                title=f"Нетипичная частота переключений: {label}",
                explanation=(
                    f"За последние {options.rolling_window_hours} ч устройство сработало "
                    f"{int(latest_count)} раз при среднем {mean_value:.1f}±{std_value:.1f}. "
                    f"Отклонение z={z_value:.2f}."
                ),
                detected_at=latest_ts.to_pydatetime(),
                related_event_ids=event_ids,
                metrics={
                    "reason": "rolling activity frequency changed significantly",
                    "semanticReason": str(entity_rows["semantic_reason"].iloc[-1]),
                    "zScore": round(float(z_value), 3),
                    "windowCount": int(latest_count),
                    "rollingMean": round(mean_value, 3),
                    "rollingStd": round(std_value, 3),
                },
            )
        )
    return results


def _detect_numeric_spikes(
    frame: pd.DataFrame,
    options: AnomalyDetectionOptions,
) -> list[_RawAnomaly]:
    results: list[_RawAnomaly] = []
    numeric = frame.copy()
    numeric["value"] = pd.to_numeric(numeric["new_state"], errors="coerce")
    numeric = numeric.dropna(subset=["value"])
    if numeric.empty:
        return results

    for entity_id, entity_rows in numeric.groupby("entity_id"):
        if len(entity_rows) < options.min_events_per_entity:
            continue

        values = entity_rows["value"].astype(float)
        deltas = values.diff().abs().dropna()
        if not deltas.empty and float(deltas.median()) < options.min_numeric_delta:
            continue
        rolling_mean = values.rolling(
            window=options.rolling_window_hours,
            min_periods=options.min_hourly_samples,
        ).mean()
        rolling_std = values.rolling(
            window=options.rolling_window_hours,
            min_periods=options.min_hourly_samples,
        ).std(ddof=0)

        latest = entity_rows.iloc[-1]
        mean_value = float(rolling_mean.iloc[-1])
        std_value = float(rolling_std.iloc[-1]) if not np.isnan(rolling_std.iloc[-1]) else 0.0
        if std_value <= 0:
            continue

        latest_value = float(latest["value"])
        z_value = (latest_value - mean_value) / std_value
        if abs(z_value) < options.z_score_threshold:
            continue

        score = _zscore_to_score(abs(z_value), options.z_score_threshold)
        direction = "рост" if z_value > 0 else "падение"
        results.append(
            _RawAnomaly(
                entity_id=str(entity_id),
                anomaly_type=TYPE_CONSUMPTION_SPIKE,
                method=METHOD_Z_SCORE,
                score=score,
                title=f"Резкий {direction} показателя: {latest['label']}",
                explanation=(
                    f"Значение {latest_value:.2g} отличается от скользящего среднего "
                    f"{mean_value:.2g}±{std_value:.2g} (z={z_value:.2f}). "
                    f"Предыдущее: {latest['old_state']}."
                ),
                detected_at=latest["time_fired_utc"].to_pydatetime(),
                related_event_ids=[int(latest["id"])],
                metrics={
                    "reason": "numeric value exceeded historical rolling baseline",
                    "semanticReason": str(latest.get("semantic_reason", "")),
                    "zScore": round(float(z_value), 3),
                    "latestValue": latest_value,
                    "rollingMean": round(mean_value, 3),
                    "rollingStd": round(std_value, 3),
                },
            )
        )
    return results


def _detect_isolation_forest(
    frame: pd.DataFrame,
    options: AnomalyDetectionOptions,
) -> list[_RawAnomaly]:
    results: list[_RawAnomaly] = []
    frame = _behavior_frame(frame)
    if frame.empty:
        return results
    features = (
        frame.groupby(["entity_id", "hour_bucket"], as_index=False)
        .agg(
            count=("id", "size"),
            hour=("hour", "first"),
            dow=("dow", "first"),
            ids=("id", list),
            label=("label", "first"),
            semantic_reason=("semantic_reason", "first"),
        )
        .sort_values("hour_bucket")
    )
    if len(features) < options.isolation_forest_min_samples:
        return results

    matrix = features[["count", "hour", "dow"]].astype(float).to_numpy()
    model = IsolationForest(
        n_estimators=options.isolation_forest_estimators,
        contamination=options.isolation_forest_contamination,
        random_state=42,
        n_jobs=1,
    )
    predictions = model.fit_predict(matrix)
    scores = -model.score_samples(matrix)
    normalized = _normalize_scores(scores)

    for idx, prediction in enumerate(predictions):
        if prediction != -1:
            continue
        row = features.iloc[idx]
        score = float(normalized[idx])
        if score < options.min_score:
            continue
        results.append(
            _RawAnomaly(
                entity_id=str(row["entity_id"]),
                anomaly_type=TYPE_DEVICE_BEHAVIOR,
                method=METHOD_ISOLATION_FOREST,
                score=score,
                title=f"Многомерная аномалия поведения: {row['label']}",
                explanation=(
                    f"Isolation Forest выделил сочетание "
                    f"{int(row['count'])} событий/ч, час {int(row['hour']):02d}, "
                    f"день недели {int(row['dow'])} как нетипичное "
                    f"(оценка {score:.0%})."
                ),
                detected_at=row["hour_bucket"].to_pydatetime().replace(tzinfo=UTC),
                related_event_ids=[int(v) for v in row["ids"][:20]],
                metrics={
                    "reason": "count/hour/weekday combination is outside behavioral baseline",
                    "semanticReason": str(row.get("semantic_reason", "")),
                    "isolationScore": round(score, 4),
                    "hourlyCount": int(row["count"]),
                    "hour": int(row["hour"]),
                    "dayOfWeek": int(row["dow"]),
                },
            )
        )
    return results


def _merge_candidates(
    candidates: list[_RawAnomaly],
    options: AnomalyDetectionOptions,
) -> list[_RawAnomaly]:
    if not candidates:
        return []

    best: dict[str, _RawAnomaly] = {}
    for item in candidates:
        if item.score < options.min_score:
            continue
        bucket = item.detected_at.strftime("%Y%m%d%H")
        key = f"{item.entity_id}|{item.anomaly_type}|{bucket}"
        current = best.get(key)
        if current is None or item.score > current.score:
            best[key] = item
    return list(best.values())


def _to_item(raw: _RawAnomaly, options: AnomalyDetectionOptions) -> AnomalyItem:
    severity = _score_to_severity(raw, options)
    detection_id = hashlib.sha256(
        f"{raw.entity_id}|{raw.anomaly_type}|{raw.detected_at.isoformat()}".encode()
    ).hexdigest()[:16]
    return AnomalyItem(
        id=detection_id,
        entity_id=raw.entity_id,
        anomaly_type=raw.anomaly_type,
        severity=severity,
        score=round(raw.score, 4),
        method=raw.method,
        title=raw.title,
        explanation=raw.explanation,
        detected_at_utc=raw.detected_at,
        related_event_ids=raw.related_event_ids,
        metrics=raw.metrics,
    )


def _score_to_severity(raw: _RawAnomaly, options: AnomalyDetectionOptions) -> str:
    score = raw.score
    security_entity = any(
        marker in raw.entity_id
        for marker in ("lock.", "alarm_control_panel.", "smoke", "gas", "moisture", "water")
    )
    if security_entity and score >= options.medium_severity_threshold:
        return SEVERITY_HIGH
    if score >= options.high_severity_threshold:
        return SEVERITY_HIGH
    if score >= options.medium_severity_threshold:
        return SEVERITY_MEDIUM
    return SEVERITY_LOW


def _behavior_frame(frame: pd.DataFrame) -> pd.DataFrame:
    if frame.empty:
        return frame
    return frame[
        (frame["can_trigger"].astype(bool) | frame["can_action"].astype(bool))
        & (frame["role"].isin(["sensor", "actuator", "hybrid"]))
    ].copy()


def _zscore_to_score(z_value: float, threshold: float) -> float:
    normalized = (z_value - threshold) / max(threshold, 1.0)
    return float(np.clip(0.55 + normalized * 0.2, 0.0, 1.0))


def _ratio_to_score(probability: float, max_ratio: float) -> float:
    if probability >= max_ratio:
        return 0.0
    return float(np.clip(1.0 - probability / max(max_ratio, 1e-6), 0.0, 1.0))


def _normalize_scores(values: np.ndarray) -> np.ndarray:
    if len(values) == 0:
        return values
    min_value = float(np.min(values))
    max_value = float(np.max(values))
    if max_value - min_value < 1e-9:
        return np.full_like(values, 0.6, dtype=float)
    return (values - min_value) / (max_value - min_value)
