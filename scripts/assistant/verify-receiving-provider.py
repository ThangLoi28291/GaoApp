"""Read-only measurement of the real receiving requests; never print keys or prompts."""
import json
import sqlite3
from pathlib import Path

evidence = Path(__file__).resolve().parents[2] / 'Logs' / 'assistant-integration-20261012'
report = json.loads((evidence / 'receiving-ai-results/live-gemini-receiving-results.json').read_text(encoding='utf-8'))
ids = [item['requestId'] for item in report['details'] if item['status'] != 'multiple_items_not_supported']
runtime = json.loads((evidence / 'local-ui/runtime.json').read_text(encoding='utf-8-sig'))
connection = sqlite3.connect(Path(runtime['AssistantOperations']['DatabasePath']).as_uri() + '?mode=ro', uri=True)
connection.row_factory = sqlite3.Row
rows = [dict(row) for row in connection.execute(
    'SELECT RequestId, Stage, Provider, Model, Status, TotalTokens FROM AiCalls '
    f"WHERE RequestId IN ({','.join('?' for _ in ids)}) ORDER BY StartedMs", ids)]
connection.close()
assert len(rows) == len(ids), 'Every receiving name request must have a measured model call.'
assert all(row['Provider'].lower() == 'gemini' and row['Stage'] == 'receiving_routing' and row['Status'] == 'succeeded' for row in rows)
summary = {'realGeminiReceivingCalls': len(rows), 'allSucceeded': True, 'model': runtime['Gemini']['Model'], 'calls': rows}
(evidence / 'receiving-ai-results/real-receiving-provider.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in summary.items() if k != 'calls'}))
