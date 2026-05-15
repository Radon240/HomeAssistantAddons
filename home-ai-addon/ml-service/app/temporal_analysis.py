from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone

import numpy as np
import pandas as pd
from river import stats

CADENCE_HOURLY = "hourly"
CADENCE_DAILY = "daily"
CADENCE_WEEKLY = "weekly"
CADENCE_MONTHLY = "monthly"
CADENCE_IRREGULAR = "irregular"

WEEKDAY_NAMES_RU = (
    "понедельникам",
    "вторникам",
    "средам",
    "четвергам",
    "пятницам",
    "субботам",
    "воскресеньям",
)

WEEKDAY_SHORT_RU = ("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")


@dataclass(frozen=True)
class CadenceResult:
    cadence: str
    confidence: float
    schedule_hint: str
    interval_hours: float | None


def detect_cadence(occurrence_times: list[datetime]) -> CadenceResult:
    if len(occurrence_times) < 3:
        return CadenceResult(
            cadence=CADENCE_IRREGULAR,
            confidence=0.0,
            schedule_hint="Недостаточно повторений для расписания",
            interval_hours=None,
        )

    times = sorted(_normalize(t) for t in occurrence_times)
    frame = pd.DataFrame({"ts": times})
    frame["hour"] = frame["ts"].dt.hour
    frame["weekday"] = frame["ts"].dt.weekday
    frame["date"] = frame["ts"].dt.date
    frame["week"] = frame["ts"].dt.to_period("W").astype(str)

    deltas = np.diff(frame["ts"].astype(np.int64) // 10**9)
    candidates: list[CadenceResult] = []

    interval_result = _detect_interval_cadence(deltas, times)
    if interval_result is not None:
        candidates.append(interval_result)

    candidates.append(_detect_daily_calendar(frame))
    candidates.append(_detect_weekly_calendar(frame))
    candidates.append(_detect_hourly_calendar(frame, deltas))

    best = max(candidates, key=lambda item: item.confidence)
    if best.confidence < 0.35:
        return CadenceResult(
            cadence=CADENCE_IRREGULAR,
            confidence=round(best.confidence, 4),
            schedule_hint="Повторения есть, но без устойчивого расписания",
            interval_hours=best.interval_hours,
        )
    return best


def _detect_interval_cadence(deltas: np.ndarray, times: list[datetime]) -> CadenceResult | None:
    if len(deltas) < 2:
        return None

    positive = deltas[deltas > 0]
    if len(positive) == 0:
        return None

    median_seconds = float(np.median(positive))
    mean_seconds = float(np.mean(positive))
    std_seconds = float(np.std(positive))
    cv = std_seconds / mean_seconds if mean_seconds > 0 else 1.0
    regularity = 1.0 / (1.0 + cv)
    median_hours = median_seconds / 3600.0

    if 0.67 <= median_hours <= 1.5:
        return CadenceResult(
            cadence=CADENCE_HOURLY,
            confidence=round(min(0.95, regularity * 0.9 + 0.1), 4),
            schedule_hint="Примерно каждый час",
            interval_hours=median_hours,
        )
    if 20.0 <= median_hours <= 28.0:
        peak_hour = _peak_hour_river(times)
        return CadenceResult(
            cadence=CADENCE_DAILY,
            confidence=round(min(0.95, regularity * 0.85 + 0.15), 4),
            schedule_hint=f"Примерно раз в сутки, около {peak_hour:02d}:00 UTC",
            interval_hours=median_hours,
        )
    if 144.0 <= median_hours <= 192.0:
        weekday = pd.Timestamp(times[-1]).weekday()
        peak_hour = _peak_hour_river(times)
        return CadenceResult(
            cadence=CADENCE_WEEKLY,
            confidence=round(min(0.95, regularity * 0.85 + 0.15), 4),
            schedule_hint=(
                f"Примерно раз в неделю, по {WEEKDAY_NAMES_RU[weekday]} около {peak_hour:02d}:00 UTC"
            ),
            interval_hours=median_hours,
        )
    if 648.0 <= median_hours <= 792.0:
        return CadenceResult(
            cadence=CADENCE_MONTHLY,
            confidence=round(min(0.9, regularity * 0.8 + 0.1), 4),
            schedule_hint="Примерно раз в месяц",
            interval_hours=median_hours,
        )
    return None


def _detect_daily_calendar(frame: pd.DataFrame) -> CadenceResult:
    best_score = 0.0
    best_hour = 0
    for hour, group in frame.groupby("hour"):
        unique_days = group["date"].nunique()
        score = unique_days / max(len(frame), 1)
        if unique_days >= 3 and score > best_score:
            best_score = score
            best_hour = int(hour)

    confidence = min(0.95, best_score * 1.2) if best_score > 0 else 0.0
    if confidence < 0.4:
        return CadenceResult(CADENCE_IRREGULAR, confidence, "", None)

    return CadenceResult(
        cadence=CADENCE_DAILY,
        confidence=round(confidence, 4),
        schedule_hint=f"Часто около {best_hour:02d}:00 UTC в разные дни",
        interval_hours=24.0,
    )


def _detect_weekly_calendar(frame: pd.DataFrame) -> CadenceResult:
    best_score = 0.0
    best_weekday = 0
    best_hour = 0
    frame = frame.copy()
    frame["hour_bucket"] = frame["hour"] // 2 * 2

    for (weekday, hour_bucket), group in frame.groupby(["weekday", "hour_bucket"]):
        unique_weeks = group["week"].nunique()
        score = unique_weeks / max(len(frame), 1)
        if unique_weeks >= 2 and score > best_score:
            best_score = score
            best_weekday = int(weekday)
            best_hour = int(hour_bucket)

    confidence = min(0.92, best_score * 1.3) if best_score > 0 else 0.0
    if confidence < 0.4:
        return CadenceResult(CADENCE_IRREGULAR, confidence, "", None)

    return CadenceResult(
        cadence=CADENCE_WEEKLY,
        confidence=round(confidence, 4),
        schedule_hint=(
            f"По {WEEKDAY_NAMES_RU[best_weekday]} около {best_hour:02d}:00 UTC"
        ),
        interval_hours=168.0,
    )


def _detect_hourly_calendar(frame: pd.DataFrame, deltas: np.ndarray) -> CadenceResult:
    minute_counts: dict[int, stats.Sum] = {}
    for ts in frame["ts"]:
        minute = int(ts.minute // 15) * 15
        if minute not in minute_counts:
            minute_counts[minute] = stats.Sum()
        minute_counts[minute].update(1)

    if not minute_counts:
        return CadenceResult(CADENCE_IRREGULAR, 0.0, "", None)

    peak_minute = max(minute_counts, key=lambda m: minute_counts[m].get())
    peak_hits = minute_counts[peak_minute].get()
    minute_score = peak_hits / max(len(frame), 1)

    hourly_deltas = deltas[(deltas >= 2400) & (deltas <= 5400)]
    delta_score = len(hourly_deltas) / max(len(deltas), 1) if len(deltas) else 0.0

    confidence = min(0.9, minute_score * 0.5 + delta_score * 0.5)
    if confidence < 0.4:
        return CadenceResult(CADENCE_IRREGULAR, confidence, "", None)

    return CadenceResult(
        cadence=CADENCE_HOURLY,
        confidence=round(confidence, 4),
        schedule_hint=f"Примерно каждый час (часто в :{peak_minute:02d} UTC)",
        interval_hours=1.0,
    )


def _peak_hour_river(times: list[datetime]) -> int:
    hour_stats: dict[int, stats.Sum] = {}
    for ts in times:
        hour = ts.hour
        if hour not in hour_stats:
            hour_stats[hour] = stats.Sum()
        hour_stats[hour].update(1)
    return max(hour_stats, key=lambda h: hour_stats[h].get())


def _normalize(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


def weekday_concentration(occurrence_times: list[datetime]) -> tuple[float, str | None]:
    """Return (concentration 0..1, hint) for dominant weekday or weekday/workday split."""
    if len(occurrence_times) < 2:
        return 0.0, None

    weekdays = [_normalize(t).weekday() for t in occurrence_times]
    total = len(weekdays)
    counts: dict[int, int] = {}
    for day in weekdays:
        counts[day] = counts.get(day, 0) + 1

    best_day, best_count = max(counts.items(), key=lambda item: item[1])
    concentration = best_count / total

    weekdays_only = sum(1 for day in weekdays if day < 5)
    weekend_only = total - weekdays_only
    workday_ratio = weekdays_only / total

    if workday_ratio >= 0.75 and concentration < 0.55:
        return round(workday_ratio, 4), "чаще по будням"

    if workday_ratio <= 0.25:
        return round(1.0 - workday_ratio, 4), "чаще по выходным"

    if concentration >= 0.45:
        return round(concentration, 4), f"чаще по {WEEKDAY_NAMES_RU[best_day]}"

    return round(concentration, 4), None


def cadence_label_ru(cadence: str) -> str:
    labels = {
        CADENCE_HOURLY: "Каждый час",
        CADENCE_DAILY: "Ежедневно",
        CADENCE_WEEKLY: "Еженедельно",
        CADENCE_MONTHLY: "Ежемесячно",
        CADENCE_IRREGULAR: "Без расписания",
    }
    return labels.get(cadence, cadence)
