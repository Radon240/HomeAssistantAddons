from __future__ import annotations

from dataclasses import dataclass

from app.models import AnalyzeOptions, ExplanationFactor
from app.pattern_miner import PatternCandidate
from app.temporal_analysis import CADENCE_IRREGULAR, CadenceResult


@dataclass(frozen=True)
class ScoredPattern:
    base_confidence: float
    lift: float
    support_ratio: float
    gap_stability: float
    weekday_score: float
    cadence_score: float
    why_generated: str
    explanation_factors: tuple[ExplanationFactor, ...]


def score_pattern(
    candidate: PatternCandidate,
    cadence_result: CadenceResult,
    session_count: int,
    options: AnalyzeOptions,
) -> ScoredPattern:
    session_count = max(session_count, 1)
    support_ratio = candidate.support_count / session_count
    gap_stability = _gap_stability_score(candidate.median_step_gaps, options.max_step_gap_seconds)
    weekday_score = candidate.weekday_concentration

    cadence_score = (
        cadence_result.confidence if cadence_result.cadence != CADENCE_IRREGULAR else 0.35
    )

    association = candidate.confidence
    lift_norm = _normalize_lift(candidate.lift, options.min_lift)

    base_confidence = round(
        0.25 * association
        + 0.20 * lift_norm
        + 0.15 * min(1.0, support_ratio * 5.0)
        + 0.15 * weekday_score
        + 0.10 * gap_stability
        + 0.15 * cadence_score,
        4,
    )

    factors: list[ExplanationFactor] = [
        ExplanationFactor(
            key="association",
            label="Последовательность",
            value=f"Шаги повторяются вместе в {int(association * 100)}% случаев после префикса",
            weight=0.25,
            score=round(association, 4),
        ),
        ExplanationFactor(
            key="lift",
            label="Сила связи (lift)",
            value=f"В {candidate.lift:.1f}× чаще, чем случайное совпадение",
            weight=0.20,
            score=round(lift_norm, 4),
        ),
        ExplanationFactor(
            key="support",
            label="Повторяемость",
            value=f"{candidate.support_count} сессий из {session_count} ({int(support_ratio * 100)}%)",
            weight=0.15,
            score=round(min(1.0, support_ratio * 5.0), 4),
        ),
    ]

    if weekday_score >= 0.5:
        factors.append(
            ExplanationFactor(
                key="weekday",
                label="Дни недели",
                value=candidate.weekday_hint or "Устойчивый день недели",
                weight=0.15,
                score=round(weekday_score, 4),
            )
        )

    if candidate.median_step_gaps:
        gaps_text = ", ".join(f"{int(g)}с" for g in candidate.median_step_gaps)
        factors.append(
            ExplanationFactor(
                key="step_gaps",
                label="Интервалы между шагами",
                value=f"Обычно через {gaps_text}",
                weight=0.10,
                score=round(gap_stability, 4),
            )
        )

    if cadence_result.cadence != CADENCE_IRREGULAR:
        factors.append(
            ExplanationFactor(
                key="schedule",
                label="Расписание",
                value=cadence_result.schedule_hint,
                weight=0.15,
                score=round(cadence_score, 4),
            )
        )

    why_parts = [
        f"сценарий из {len(candidate.entity_keys)} шагов",
        f"повторился {candidate.support_count} раз",
    ]
    if candidate.lift >= options.min_lift:
        why_parts.append(f"lift {candidate.lift:.1f}")
    if cadence_result.cadence != CADENCE_IRREGULAR:
        why_parts.append(cadence_result.schedule_hint.lower())
    elif candidate.weekday_hint:
        why_parts.append(candidate.weekday_hint.lower())

    return ScoredPattern(
        base_confidence=base_confidence,
        lift=candidate.lift,
        support_ratio=round(support_ratio, 4),
        gap_stability=gap_stability,
        weekday_score=weekday_score,
        cadence_score=cadence_score,
        why_generated="; ".join(why_parts).capitalize() + ".",
        explanation_factors=tuple(factors),
    )


def passes_quality_gates(
    candidate: PatternCandidate,
    scored: ScoredPattern,
    options: AnalyzeOptions,
) -> bool:
    if candidate.support_count < options.min_support:
        return False
    if scored.base_confidence < options.min_confidence:
        return False
    if candidate.lift < options.min_lift:
        return False
    if scored.support_ratio < options.min_support_ratio:
        return False
    return True


def _normalize_lift(lift: float, min_lift: float) -> float:
    if lift <= 1.0:
        return max(0.0, lift - 0.5)
    span = max(min_lift, 1.01)
    return min(1.0, (lift - 1.0) / (span - 1.0 + 1.0))


def _gap_stability_score(median_gaps: tuple[float, ...], max_gap: float) -> float:
    if not median_gaps:
        return 0.5
    limit = max(max_gap, 1)
    scores = [max(0.0, 1.0 - gap / limit) for gap in median_gaps]
    return sum(scores) / len(scores)
