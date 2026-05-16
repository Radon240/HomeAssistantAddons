from __future__ import annotations

from collections import Counter, defaultdict
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
    weight: float


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
    weighted_support: float = 0.0
    weighted_confidence: float = 0.0
    intent_score: float = 0.0
    automation_origin_ratio: float = 0.0
    existing_automation_score: float = 0.0
    existing_automation_reason: str = ""
    negative_evidence: float = 0.0
    semantic_score: float = 0.0
    semantic_reason: str = ""
    area_score: float = 0.5
    area_hint: str | None = None
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
                        weight=_pattern_weight(pattern_tokens),
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
                occurrence_weight = _pattern_weight(pattern_tokens)
                token_lookup.setdefault(pattern_key, pattern_tokens)
                records.append(
                    {
                        "session_id": session_id,
                        "length": length,
                        "pattern_key": pattern_key,
                        "entity_keys": pattern_keys,
                        "labels": tuple(labels[start : start + length]),
                        "occurrence_weight": occurrence_weight,
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
                "weighted_support",
                "length",
            ]
        )

    frame = pd.DataFrame(records)
    grouped = (
        frame.groupby("pattern_key", as_index=False)
        .agg(
            support_count=("session_id", "nunique"),
            weighted_support=("occurrence_weight", "sum"),
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
    prefix_weight: dict[tuple[str, ...], float] = {}
    suffix_support: dict[str, int] = {}

    for _, row in frame.iterrows():
        entity_keys: tuple[str, ...] = tuple(row["entity_keys"])
        if len(entity_keys) <= 1:
            continue
        prefix = entity_keys[:-1]
        prefix_key = "|".join(prefix)
        count = int(row["support_count"])
        weight = float(row["weighted_support"])
        prefix_support[prefix_key] = max(prefix_support.get(prefix_key, 0), count)
        prefix_weight[prefix_key] = max(prefix_weight.get(prefix_key, 0.0), weight)

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
        weighted_support = round(float(row["weighted_support"]), 4)

        prefix = entity_keys[:-1]
        prefix_key = "|".join(prefix)
        prefix_count = max(
            prefix_support.get(prefix_key, 0),
            tracker.prefix_count(prefix),
            1,
        )
        confidence = support / prefix_count
        weighted_confidence = weighted_support / max(prefix_weight.get(prefix_key, 0.0), weighted_support, 1e-6)
        negative_evidence = max(0.0, (prefix_count - support) / max(prefix_count, 1))

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
        area_score, area_hint = _area_context(row["tokens"])
        intent_score = _intent_score(row["tokens"])
        automation_origin_ratio = _automation_origin_ratio(row["tokens"])
        existing_score, existing_reason = _existing_automation_score(row["tokens"])

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
                weighted_support=weighted_support,
                weighted_confidence=round(weighted_confidence, 4),
                intent_score=intent_score,
                automation_origin_ratio=automation_origin_ratio,
                existing_automation_score=existing_score,
                existing_automation_reason=existing_reason,
                negative_evidence=round(negative_evidence, 4),
                semantic_score=semantic_score,
                semantic_reason=semantic_reason,
                area_score=area_score,
                area_hint=area_hint,
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
                weighted_support=candidate.weighted_support,
                weighted_confidence=candidate.weighted_confidence,
                intent_score=candidate.intent_score,
                automation_origin_ratio=candidate.automation_origin_ratio,
                existing_automation_score=candidate.existing_automation_score,
                existing_automation_reason=candidate.existing_automation_reason,
                negative_evidence=candidate.negative_evidence,
                semantic_score=candidate.semantic_score,
                semantic_reason=candidate.semantic_reason,
                area_score=candidate.area_score,
                area_hint=candidate.area_hint,
                occurrence_times=candidate.occurrence_times,
                median_step_gaps=candidate.median_step_gaps,
                weekday_concentration=candidate.weekday_concentration,
                weekday_hint=candidate.weekday_hint,
            )
        )

    enriched.sort(
        key=lambda c: (c.lift, c.area_score, c.confidence, c.support_count, c.frequency_score),
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


def _pattern_weight(tokens: tuple[ActionToken, ...]) -> float:
    if not tokens:
        return 0.0
    weights = [token.intelligence.event_weight for token in tokens]
    return round(sum(weights) / len(weights), 4)


def _intent_score(tokens: tuple[ActionToken, ...]) -> float:
    if not tokens:
        return 0.0
    scores = [token.intelligence.intent_score for token in tokens]
    trigger_bonus = 0.1 if tokens[0].intelligence.intent_score >= 0.6 else 0.0
    return round(min(1.0, sum(scores) / len(scores) + trigger_bonus), 4)


def _automation_origin_ratio(tokens: tuple[ActionToken, ...]) -> float:
    if not tokens:
        return 0.0
    generated = sum(
        1
        for token in tokens
        if token.intelligence.origin.value in {"automation", "cascade"}
    )
    return round(generated / len(tokens), 4)


def _existing_automation_score(tokens: tuple[ActionToken, ...]) -> tuple[float, str]:
    if len(tokens) < 2:
        return 0.0, ""

    trigger = tokens[0]
    actions = tokens[1:]
    trigger_context = trigger.context_id
    if trigger_context:
        propagated = sum(1 for token in actions if token.context_parent_id == trigger_context)
        if propagated:
            score = propagated / len(actions)
            return round(score, 4), "Action-события имеют parent_id trigger context."

    parent_ids = [token.context_parent_id for token in tokens if token.context_parent_id]
    if parent_ids:
        parent, count = Counter(parent_ids).most_common(1)[0]
        score = count / len(tokens)
        if score >= 0.5:
            return round(score, 4), f"События связаны одним automation parent context {parent}."

    automation_ratio = _automation_origin_ratio(tokens)
    if automation_ratio >= 0.5:
        return automation_ratio, "Большая часть sequence сгенерирована automation/cascade."

    return 0.0, ""


def _area_context(tokens: tuple[ActionToken, ...]) -> tuple[float, str | None]:
    named_areas = [token.area_name or token.area_id for token in tokens if token.area_name or token.area_id]
    if len(named_areas) < 2:
        return 0.5, None

    first = named_areas[0]
    same_area_count = sum(1 for area in named_areas if area == first)
    if same_area_count == len(named_areas):
        return 1.0, f"Все шаги находятся в зоне «{first}»."

    unique = sorted(set(named_areas))
    if len(unique) <= 2:
        return 0.75, "Сценарий связывает соседние зоны: " + ", ".join(unique) + "."

    short_gaps = all(gap <= 180 for gap in _step_gaps(tokens))
    movement_like = (
        short_gaps
        and tokens[0].semantics.can_trigger
        and any(token.semantics.can_action for token in tokens[1:])
        and _automation_origin_ratio(tokens) <= 0.34
    )
    if movement_like:
        return 0.65, "Похоже на естественный multi-room путь: " + " → ".join(named_areas[:5]) + "."

    return 0.4, "Сценарий затрагивает много разных зон: " + ", ".join(unique[:4]) + "."


def _is_contiguous_subsequence(short: tuple[str, ...], long: tuple[str, ...]) -> bool:
    if len(short) >= len(long):
        return False
    for index in range(len(long) - len(short) + 1):
        if long[index : index + len(short)] == short:
            return True
    return False
