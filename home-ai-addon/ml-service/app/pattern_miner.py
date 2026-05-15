from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime

import pandas as pd
from sklearn.preprocessing import minmax_scale

from app.river_tracker import OnlinePatternTracker
from app.sequence_builder import ActionToken


@dataclass(frozen=True)
class PatternCandidate:
    labels: tuple[str, ...]
    support_count: int
    prefix_support: int
    confidence: float
    frequency_score: float
    tokens: tuple[ActionToken, ...]
    occurrence_times: tuple[datetime, ...] = field(default_factory=tuple)


def collect_pattern_occurrences(
    sessions: list[list[ActionToken]],
    max_length: int,
) -> dict[str, list[datetime]]:
    occurrences: dict[str, list[datetime]] = defaultdict(list)

    for session in sessions:
        labels = [token.label for token in session]
        for length in range(2, min(max_length, len(labels)) + 1):
            for start in range(0, len(labels) - length + 1):
                pattern_key = "|".join(labels[start : start + length])
                occurrences[pattern_key].append(session[start].occurred_at)

    return occurrences


def extract_ngram_counts(sessions: list[list[ActionToken]], max_length: int) -> pd.DataFrame:
    records: list[dict[str, object]] = []
    token_lookup: dict[str, tuple[ActionToken, ...]] = {}

    for session_id, session in enumerate(sessions):
        labels = [token.label for token in session]
        for length in range(2, min(max_length, len(labels)) + 1):
            for start in range(0, len(labels) - length + 1):
                pattern_labels = tuple(labels[start : start + length])
                pattern_key = "|".join(pattern_labels)
                pattern_tokens = tuple(session[start : start + length])
                token_lookup.setdefault(pattern_key, pattern_tokens)
                records.append(
                    {
                        "session_id": session_id,
                        "length": length,
                        "pattern_key": pattern_key,
                        "labels": pattern_key,
                    }
                )

    if not records:
        return pd.DataFrame(
            columns=["pattern_key", "labels", "tokens", "support_count", "length"]
        )

    frame = pd.DataFrame(records)
    grouped = (
        frame.groupby("pattern_key", as_index=False)
        .agg(
            support_count=("session_id", "nunique"),
            length=("length", "max"),
            labels=("labels", "first"),
        )
        .sort_values(["support_count", "length"], ascending=[False, False])
    )
    grouped["tokens"] = grouped["pattern_key"].map(token_lookup)
    grouped["labels"] = grouped["pattern_key"].str.split("|").map(tuple)
    return grouped


def mine_patterns(
    sessions: list[list[ActionToken]],
    min_support: int,
    max_sequence_length: int,
    tracker: OnlinePatternTracker,
) -> list[PatternCandidate]:
    if not sessions:
        return []

    label_sessions = [[token.label for token in session] for session in sessions]
    tracker.observe_sessions(label_sessions)

    frame = extract_ngram_counts(sessions, max_sequence_length)
    occurrence_map = collect_pattern_occurrences(sessions, max_sequence_length)
    if frame.empty:
        return []

    prefix_support: dict[tuple[str, ...], int] = {}
    for _, row in frame.iterrows():
        labels: tuple[str, ...] = tuple(row["labels"])
        if len(labels) <= 1:
            continue
        prefix = labels[:-1]
        count = int(row["support_count"])
        prefix_key = "|".join(prefix)
        prefix_support[prefix_key] = max(prefix_support.get(prefix_key, 0), count)

    candidates: list[PatternCandidate] = []
    seen: set[tuple[str, ...]] = set()

    for _, row in frame.iterrows():
        labels: tuple[str, ...] = tuple(row["labels"])
        if labels in seen:
            continue
        seen.add(labels)

        support = int(row["support_count"])
        if support < min_support:
            continue

        prefix = labels[:-1]
        prefix_key = "|".join(prefix)
        prefix_count = max(
            prefix_support.get(prefix_key, 0),
            tracker.prefix_count(prefix),
            1,
        )
        confidence = support / prefix_count

        pattern_key = "|".join(labels)
        times = tuple(occurrence_map.get(pattern_key, []))

        candidates.append(
            PatternCandidate(
                labels=labels,
                support_count=support,
                prefix_support=prefix_count,
                confidence=round(confidence, 4),
                frequency_score=0.0,
                tokens=row["tokens"],
                occurrence_times=times,
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
                labels=candidate.labels,
                support_count=candidate.support_count,
                prefix_support=candidate.prefix_support,
                confidence=candidate.confidence,
                frequency_score=round(float(score), 4),
                tokens=candidate.tokens,
                occurrence_times=candidate.occurrence_times,
            )
        )

    enriched.sort(key=lambda c: (c.confidence, c.support_count, c.frequency_score), reverse=True)
    return _drop_subsequence_duplicates(enriched)


def _drop_subsequence_duplicates(candidates: list[PatternCandidate]) -> list[PatternCandidate]:
    kept: list[PatternCandidate] = []
    for candidate in candidates:
        if any(
            len(other.labels) > len(candidate.labels)
            and candidate.support_count == other.support_count
            and _is_contiguous_subsequence(candidate.labels, other.labels)
            for other in candidates
        ):
            continue
        kept.append(candidate)
    return kept


def _is_contiguous_subsequence(short: tuple[str, ...], long: tuple[str, ...]) -> bool:
    if len(short) >= len(long):
        return False
    for index in range(len(long) - len(short) + 1):
        if long[index : index + len(short)] == short:
            return True
    return False
