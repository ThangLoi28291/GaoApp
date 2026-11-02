"""Read-only provider metadata. All tested natural-language turns must have a real Gemini call."""
import json
import sqlite3
from pathlib import Path

evidence = Path(__file__).resolve().parents[2] / 'Logs' / 'assistant-integration-20261012'
report = json.loads((evidence / 'natural-ai-results/live-natural-language-results.json').read_text(encoding='utf-8'))
ids = [item['requestId'] for item in report['details']]
runtime = json.loads((evidence / 'local-ui/runtime.json').read_text(encoding='utf-8-sig'))
connection = sqlite3.connect(Path(runtime['AssistantOperations']['DatabasePath']).as_uri() + '?mode=ro', uri=True)
connection.row_factory = sqlite3.Row
rows = [dict(row) for row in connection.execute(
    'SELECT RequestId, Stage, Provider, Model, Status, TotalTokens FROM AiCalls '
    f"WHERE RequestId IN ({','.join('?' for _ in ids)}) ORDER BY StartedMs", ids)]
connection.close()
assert set(ids) == {row['RequestId'] for row in rows}, 'A natural-language turn did not call a real model.'
assert all(row['Provider'].lower() == 'gemini' and row['Status'] == 'succeeded' for row in rows)
assert {'routing', 'receiving_routing', 'name_normalization'}.issubset({row['Stage'] for row in rows})
summary = {'verifiedNaturalTurns': len(ids), 'realGeminiCalls': len(rows), 'stages': sorted({row['Stage'] for row in rows}), 'allSucceeded': True, 'calls': rows}
(evidence / 'natural-ai-results/real-natural-provider.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in summary.items() if k != 'calls'}))
