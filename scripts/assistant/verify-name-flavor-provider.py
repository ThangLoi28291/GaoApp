"""Read-only verification for isolated real Gemini name tests; no key/prompt output."""
import json
import sqlite3
from pathlib import Path

root = Path(__file__).resolve().parents[2] / 'Logs/assistant-integration-20261012/name-flavor-results/gemini-isolated'
report = json.loads((root / 'real-name-flavor-results.json').read_text(encoding='utf-8'))
metadata = json.loads((root / 'process.json').read_text(encoding='utf-8-sig'))
ids = [item['requestId'] for item in report['details']]
connection = sqlite3.connect(Path(metadata['Database']).as_uri() + '?mode=ro', uri=True)
connection.row_factory = sqlite3.Row
rows = [dict(row) for row in connection.execute(
    'SELECT RequestId, Stage, Provider, Model, Status, TotalTokens FROM AiCalls '
    f"WHERE RequestId IN ({','.join('?' for _ in ids)}) ORDER BY StartedMs", ids)]
connection.close()
assert set(ids) == {row['RequestId'] for row in rows}
assert all(row['Provider'].lower() == 'gemini' and row['Stage'] == 'name_normalization' and row['Status'] == 'succeeded' for row in rows)
summary = {'verifiedNames': len(ids), 'realGeminiCalls': len(rows), 'allSucceeded': True, 'syntheticCatalog': True, 'calls': rows}
(root / 'provider-verification.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in summary.items() if k != 'calls'}))
