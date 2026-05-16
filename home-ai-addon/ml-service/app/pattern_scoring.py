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
    weighted_support_score = min(1.0, candidate.weighted_support / max(1.0, options.min_support))
    gap_stability = _gap_stability_score(candidate.median_step_gaps, options.max_step_gap_seconds)
    weekday_score = candidate.weekday_concentration
    temporal_score = max(candidate.temporal_score, weekday_score)

    cadence_score = (
        cadence_result.confidence if cadence_result.cadence != CADENCE_IRREGULAR else 0.35
    )

    association = max(candidate.confidence, candidate.weighted_confidence)
    lift_norm = _normalize_lift(candidate.lift, options.min_lift)
    semantic_score = candidate.semantic_score
    area_score = candidate.area_score
    intent_score = candidate.intent_score
    origin_score = 1.0 - candidate.automation_origin_ratio
    existing_automation_score = 1.0 - candidate.existing_automation_score
    negative_score = 1.0 - candidate.negative_evidence

    base_confidence = round(
        0.13 * association
        + 0.12 * lift_norm
        + 0.11 * weighted_support_score
        + 0.13 * intent_score
        + 0.11 * semantic_score
        + 0.08 * area_score
        + 0.07 * temporal_score
        + 0.07 * gap_stability
        + 0.06 * cadence_score
        + 0.04 * origin_score
        + 0.04 * existing_automation_score
        + 0.04 * negative_score,
        4,
    )

    factors: list[ExplanationFactor] = [
        ExplanationFactor(
            key="association",
            label="Последовательность",
            value=f"Шаги повторяются вместе в {int(association * 100)}% weighted cases после префикса",
            weight=0.13,
            score=round(association, 4),
        ),
        ExplanationFactor(
            key="lift",
            label="Сила связи (lift)",
            value=f"В {candidate.lift:.1f}× чаще, чем случайное совпадение",
            weight=0.12,
            score=round(lift_norm, 4),
        ),
        ExplanationFactor(
            key="support",
            label="Weighted support",
            value=(
                f"{candidate.support_count} raw sessions, weighted support "
                f"{candidate.weighted_support:.2f} из {session_count}"
            ),
            weight=0.11,
            score=round(weighted_support_score, 4),
        ),
        ExplanationFactor(
            key="intent",
            label="Намерение пользователя",
            value=f"Intent score {candidate.intent_score:.2f}; automation-origin ratio {candidate.automation_origin_ratio:.2f}",
            weight=0.13,
            score=round(intent_score, 4),
        ),
        ExplanationFactor(
            key="semantic",
            label="Семантика устройств",
            value=candidate.semantic_reason,
            weight=0.11,
            score=round(semantic_score, 4),
        ),
        ExplanationFactor(
            key="area",
            label="Зоны Home Assistant",
            value=candidate.area_hint or "Нет area metadata, зона не усиливает рекомендацию",
            weight=0.08,
            score=round(area_score, 4),
        ),
        ExplanationFactor(
            key="negative_evidence",
            label="Negative evidence",
            value=f"Префикс не привёл к действию в {int(candidate.negative_evidence * 100)}% случаев",
            weight=0.04,
            score=round(negative_score, 4),
        ),
        ExplanationFactor(
            key="automation_origin",
            label="Automation origin",
            value=(
                f"Automation-origin ratio {candidate.automation_origin_ratio:.2f}; "
                f"existing automation score {candidate.existing_automation_score:.2f}. "
                f"{candidate.existing_automation_reason}"
            ),
            weight=0.08,
            score=round(min(origin_score, existing_automation_score), 4),
        ),
    ]

    if temporal_score >= 0.45:
        factors.append(
            ExplanationFactor(
                key="temporal_context",
                label="Временной контекст",
                value=candidate.temporal_hint or candidate.weekday_hint or "Устойчивый временной паттерн",
                weight=0.15,
                score=round(temporal_score, 4),
            )
        )

    if candidate.median_step_gaps:
        gaps_text = ", ".join(f"{int(g)}с" for g in candidate.median_step_gaps)
        factors.append(
            ExplanationFactor(
                key="causal_timing",
                label="Causal timing",
                value=f"Действия следуют после trigger обычно через {gaps_text}",
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
        "проходит semantic trigger/action фильтр",
        f"intent {candidate.intent_score:.2f}",
    ]
    if candidate.lift >= options.min_lift:
        why_parts.append(f"lift {candidate.lift:.1f}")
    if candidate.area_hint and candidate.area_score >= 0.75:
        why_parts.append(candidate.area_hint.lower().rstrip("."))
    if cadence_result.cadence != CADENCE_IRREGULAR:
        why_parts.append(cadence_result.schedule_hint.lower())
    elif candidate.temporal_hint or candidate.weekday_hint:
        why_parts.append((candidate.temporal_hint or candidate.weekday_hint or "").lower())

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
    if candidate.semantic_score < 0.75:
        return False
    if candidate.intent_score < 0.35:
        return False
    if candidate.automation_origin_ratio >= 0.5:
        return False
    if candidate.existing_automation_score >= 0.5:
        return False
    if candidate.negative_evidence > 0.65:
        return False
    if candidate.weighted_support < max(1.0, options.min_support * 0.45):
        return False
    if candidate.area_score < 0.35:
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
