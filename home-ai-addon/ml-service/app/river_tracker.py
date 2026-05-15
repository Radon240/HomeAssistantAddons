from __future__ import annotations

from river import stats


class OnlinePatternTracker:
    """Incremental pattern frequency tracking via River stats.Count."""

    def __init__(self) -> None:
        self._pattern_counts: dict[tuple[str, ...], stats.Count] = {}
        self._prefix_counts: dict[tuple[str, ...], stats.Count] = {}
        self._session_counter = stats.Count()

    def observe_sessions(self, sessions: list[list[str]]) -> None:
        seen_sessions: set[int] = set()
        for session_id, labels in enumerate(sessions):
            if session_id in seen_sessions:
                continue
            seen_sessions.add(session_id)
            self._session_counter.update(1)

            for length in range(2, len(labels) + 1):
                for start in range(0, len(labels) - length + 1):
                    pattern = tuple(labels[start : start + length])
                    self._touch(self._pattern_counts, pattern).update(1)
                    if length > 1:
                        prefix = pattern[:-1]
                        self._touch(self._prefix_counts, prefix).update(1)

    def pattern_count(self, pattern: tuple[str, ...]) -> int:
        counter = self._pattern_counts.get(pattern)
        return int(counter.get()) if counter is not None else 0

    def prefix_count(self, prefix: tuple[str, ...]) -> int:
        counter = self._prefix_counts.get(prefix)
        return int(counter.get()) if counter is not None else 0

    @property
    def session_count(self) -> int:
        return int(self._session_counter.get())

    @staticmethod
    def _touch(store: dict[tuple[str, ...], stats.Count], key: tuple[str, ...]) -> stats.Count:
        if key not in store:
            store[key] = stats.Count()
        return store[key]
