"""Read only aggregate metrics from the isolated acceptance DB; never prints prompts or keys."""
import json
import sqlite3
from pathlib import Path

root = Path(__file__).resolve().parents[2] / "Logs/assistant-integration-20261012"
db = sqlite3.connect((root / "chatbot-runtime/operations.db").as_uri() + "?mode=ro", uri=True)
db.row_factory = sqlite3.Row
calls = [dict(row) for row in db.execute("""
    SELECT Operation, Stage, Provider, Model, Project, Slot, Status, FailureCode,
           DurationMs, InputTokens, OutputTokens, ThinkingTokens, CachedTokens, TotalTokens
    FROM AiCalls ORDER BY StartedMs
""")]
durations = sorted(c["DurationMs"] for c in calls if c["DurationMs"] is not None)
tokens = {field: sum(c[field] for c in calls if c[field] is not None)
          for field in ["InputTokens", "OutputTokens", "ThinkingTokens", "CachedTokens", "TotalTokens"]}
summary = {
    "scope": "All real-provider attempts in the isolated acceptance runtime, including debugging before fixes",
    "calls": len(calls), "successful": sum(c["Status"] == "succeeded" for c in calls),
    "statuses": {s: sum(c["Status"] == s for c in calls) for s in sorted({c["Status"] for c in calls})},
    "tokens": tokens, "callsWithTokenUsage": sum(c["TotalTokens"] is not None for c in calls),
    "latencyMs": {"min": min(durations, default=None), "max": max(durations, default=None),
                  "median": durations[len(durations)//2] if durations else None},
    "models": sorted({c["Model"] for c in calls}), "projectsUsed": sorted({c["Project"] for c in calls if c["Project"]}),
    "callsMetadata": calls,
}
(root / "real-ai-metrics.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in summary.items() if k != "callsMetadata"}, ensure_ascii=False, indent=2))
