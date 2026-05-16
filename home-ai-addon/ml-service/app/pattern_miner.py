from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from statistics import median

import pandas as pd
from sklearn.preprocessing import minmax_scale

from app.device_semantics import is_meaningful_automation
from app.river_tracker import OnlinePatternTracker
from app.sequence_builder import ActionToken
from app.temporal_analysis import weekday_concentration


@dataclass(frozen=True)
class PatternOccurrence:
    started_at: datetime
    step_gaps_seconds: tuple[float, ...]


@dataclass(frozen=True)
class PatternCandidate:
    entity_keys: tuple[str, ...]
    labels: tuple[str, ...]
    support_count: int
    prefix_support: int
    confidence: float
    lift: float
    frequency_score: float
    tokens: tuple[ActionToken, ...]
    semantic_score: float = 0.0
    semantic_reason: str = ""
    occurrence_times: tuple[datetime, ...] = field(default_factory=tuple)
    median_step_gaps: tuple[float, ...] = field(default_factory=tuple)
    weekday_concentration: float = 0.0
    weekday_hint: str | None = None


def _step_gaps(tokens: tuple[ActionToken, ...]) -> tuple[float, ...]:
    gaps: list[float] = []
    for index in range(len(tokens) - 1):
        delta = tokens[index + 1].occurred_at - tokens[index].occurred_at
        gaps.append(max(0.0, delta.total_seconds()))
    return tuple(gaps)


def _fits_step_gaps(gaps: tuple[float, ...], max_step_gap_seconds: int) -> bool:
    if not gaps:
        return True
    return all(gap <= max_step_gap_seconds for gap in gaps)


def collect_pattern_occurrences(
    sessions: list[list[ActionToken]],
    max_length: int,
    max_step_gap_seconds: int,
) -> dict[str, list[PatternOccurrence]]:
    occurrences: dict[str, list[PatternOccurrence]] = defaultdict(list)

    for session in sessions:
        keys = [token.entity_key for token in session]
        for length in range(2, min(max_length, len(keys)) + 1):
            for start in range(0, len(keys) - length + 1):
                pattern_tokens = tuple(session[start : start + length])
                meaningful, _ = is_meaningful_automation(
                    [token.semantics for token in pattern_tokens]
                )
                if not meaningful:
                    continue
                gaps = _step_gaps(pattern_tokens)
                if not _fits_step_gaps(gaps, max_step_gap_seconds):
                    continue
                pattern_key = "|".join(keys[start : start + length])
                occurrences[pattern_key].append(
                    PatternOccurrence(
                        started_at=pattern_tokens[0].occurred_at,
                        step_gaps_seconds=gaps,
                    )
                )

    return occurrences


def _suffix_session_count(sessions: list[list[ActionToken]], suffix_key: str) -> int:
    count = 0
    for session in sessions:
        if any(token.entity_key == suffix_key for token in session):
            count += 1
    return count


def extract_ngram_counts(
    sessions: list[list[ActionToken]],
    max_length: int,
    max_step_gap_seconds: int,
) -> pd.DataFrame:
    records: list[dict[str, object]] = []
    token_lookup: dict[str, tuple[ActionToken, ...]] = {}

    for session_id, session in enumerate(sessions):
        keys = [token.entity_key for token in session]
        labels = [token.label for token in session]
        for length in range(2, min(max_length, len(keys)) + 1):
            for start in range(0, len(keys) - length + 1):
                pattern_tokens = tuple(session[start : start + length])
                meaningful, _ = is_meaningful_automation(
                    [token.semantics for token in pattern_tokens]
                )
                if not meaningful:
                    continue
                gaps = _step_gaps(pattern_tokens)
                if not _fits_step_gaps(gaps, max_step_gap_seconds):
                    continue
                pattern_keys = tuple(keys[start : start + length])
                pattern_key = "|".join(pattern_keys)
                token_lookup.setdefault(pattern_key, pattern_tokens)
                records.append(
                    {
                        "session_id": session_id,
                        "length": length,
                        "pattern_key": pattern_key,
                        "entity_keys": pattern_keys,
                        "labels": tuple(labels[start : start + length]),
                    }
                )

    if not records:
        return pd.DataFrame(
            columns=[
                "pattern_key",
                "entity_keys",
                "labels",
                "tokens",
                "support_count",
                "length",
            ]
        )

    frame = pd.DataFrame(records)
    grouped = (
        frame.groupby("pattern_key", as_index=False)
        .agg(
            support_count=("session_id", "nunique"),
            length=("length", "max"),
            entity_keys=("entity_keys", "first"),
            labels=("labels", "first"),
        )
        .sort_values(["support_count", "length"], ascending=[False, False])
    )
    grouped["tokens"] = grouped["pattern_key"].map(token_lookup)
    return grouped


def mine_patterns(
    sessions: list[list[ActionToken]],
    min_support: int,
    max_sequence_length: int,
    max_step_gap_seconds: int,
    tracker: OnlinePatternTracker,
) -> list[PatternCandidate]:
    if not sessions:
        return []

    key_sessions = [[token.entity_key for token in session] for session in sessions]
    tracker.observe_sessions(key_sessions)

    frame = extract_ngram_counts(sessions, max_sequence_length, max_step_gap_seconds)
    occurrence_map = collect_pattern_occurrences(
        sessions, max_sequence_length, max_step_gap_seconds
    )
    if frame.empty:
        return []

    session_count = len(sessions)
    prefix_support: dict[tuple[str, ...], int] = {}
    suffix_support: dict[str, int] = {}

    for _, row in frame.iterrows():
        entity_keys: tuple[str, ...] = tuple(row["entity_keys"])
        if len(entity_keys) <= 1:
            continue
        prefix = entity_keys[:-1]
        prefix_key = "|".join(prefix)
        count = int(row["support_count"])
        prefix_support[prefix_key] = max(prefix_support.get(prefix_key, 0), count)

        suffix_key = entity_keys[-1]
        suffix_support[suffix_key] = max(
            suffix_support.get(suffix_key, 0),
            _suffix_session_count(sessions, suffix_key),
        )

    candidates: list[PatternCandidate] = []
    seen: set[tuple[str, ...]] = set()

    for _, row in frame.iterrows():
        entity_keys: tuple[str, ...] = tuple(row["entity_keys"])
        labels: tuple[str, ...] = tuple(row["labels"])
        if entity_keys in seen:
            continue
        seen.add(entity_keys)

        support = int(row["support_count"])
        if support < min_support:
            continue

        prefix = entity_keys[:-1]
        prefix_key = "|".join(prefix)
        prefix_count = max(
            prefix_support.get(prefix_key, 0),
            tracker.prefix_count(prefix),
            1,
        )
        confidence = support / prefix_count

        suffix_key = entity_keys[-1]
        suffix_count = max(suffix_support.get(suffix_key, 0), 1)
        suffix_probability = suffix_count / session_count
        lift = confidence / max(suffix_probability, 1e-6)

        pattern_key = "|".join(entity_keys)
        occ_list = occurrence_map.get(pattern_key, [])
        times = tuple(item.started_at for item in occ_list)
        gap_lists = [item.step_gaps_seconds for item in occ_list if item.step_gaps_seconds]
        median_gaps = _median_gaps(gap_lists)
        weekday_score, weekday_hint = weekday_concentration(list(times))
        semantic_ok, semantic_reason = is_meaningful_automation(
            [token.semantics for token in row["tokens"]]
        )
        if not semantic_ok:
            continue
        semantic_score = _semantic_score(row["tokens"])

        candidates.append(
            PatternCandidate(
                entity_keys=entity_keys,
                labels=labels,
                support_count=support,
                prefix_support=prefix_count,
                confidence=round(confidence, 4),
                lift=round(lift, 4),
                frequency_score=0.0,
                tokens=row["tokens"],
                semantic_score=semantic_score,
                semantic_reason=semantic_reason,
                occurrence_times=times,
                median_step_gaps=median_gaps,
                weekday_concentration=weekday_score,
                weekday_hint=weekday_hint,
            )
        )

    if not candidates:
        return []

    supports = [float(c.support_count) for c in candidates]
    scaled = minmax_scale(supports) if len(supports) > 1 else [1.0]
    enriched: list[PatternCandidate] = []
    for candidate, score in zip(candidates, scaled, strict=True):
        enriched.append(
            PatternCandidate(
                entity_keys=candidate.entity_keys,
                labels=candidate.labels,
                support_count=candidate.support_count,
                prefix_support=candidate.prefix_support,
                confidence=candidate.confidence,
                lift=candidate.lift,
                frequency_score=round(float(score), 4),
                tokens=candidate.tokens,
                semantic_score=candidate.semantic_score,
                semantic_reason=candidate.semantic_reason,
                occurrence_times=candidate.occurrence_times,
                median_step_gaps=candidate.median_step_gaps,
                weekday_concentration=candidate.weekday_concentration,
                weekday_hint=candidate.weekday_hint,
            )
        )

    enriched.sort(
        key=lambda c: (c.lift, c.confidence, c.support_count, c.frequency_score),
        reverse=True,
    )
    return _drop_subsequence_duplicates(enriched)


def _median_gaps(gap_lists: list[tuple[float, ...]]) -> tuple[float, ...]:
    if not gap_lists:
        return ()
    max_len = max(len(item) for item in gap_lists)
    result: list[float] = []
    for index in range(max_len):
        values = [item[index] for item in gap_lists if len(item) > index]
        if values:
            result.append(round(float(median(values)), 1))
    return tuple(result)


def _drop_subsequence_duplicates(candidates: list[PatternCandidate]) -> list[PatternCandidate]:
    kept: list[PatternCandidate] = []
    for candidate in candidates:
        if any(
            len(other.entity_keys) > len(candidate.entity_keys)
            and candidate.support_count == other.support_count
            and _is_contiguous_subsequence(candidate.entity_keys, other.entity_keys)
            for other in candidates
        ):
            continue
        kept.append(candidate)
    return kept


def _semantic_score(tokens: tuple[ActionToken, ...]) -> float:
    if len(tokens) < 2:
        return 0.0
    trigger = tokens[0].semantics
    actions = [token.semantics for token in tokens[1:]]
    score = 0.55
    if trigger.can_trigger:
        score += 0.2
    if all(action.can_action for action in actions):
        score += 0.2
    if trigger.intent.value in {"user_action", "environment_trigger"}:
        score += 0.05
    return round(min(1.0, score), 4)


def _is_contiguous_subsequence(short: tuple[str, ...], long: tuple[str, ...]) -> bool:
    if len(short) >= len(long):
        return False
    for index in range(len(long) - len(short) + 1):
        if long[index : index + len(short)] == short:
            return True
    return False
