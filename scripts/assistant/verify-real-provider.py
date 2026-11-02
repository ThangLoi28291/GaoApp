"""Read only provider metrics for this acceptance run; never read keys or prompts."""
import json
import sqlite3
from pathlib import Path

evidence = Path(__file__).resolve().parents[2] / 'Logs' / 'assistant-integration-20261012'
report = json.loads((evidence / 'live-gemini-all-results.json').read_text(encoding='utf-8'))
request_ids = sorted({item['requestId'] for item in report['details'] if item.get('requestId')})
runtime = json.loads((evidence / 'local-ui' / 'runtime.json').read_text(encoding='utf-8-sig'))
connection = sqlite3.connect(Path(runtime['AssistantOperations']['DatabasePath']).as_uri() + '?mode=ro', uri=True)
connection.row_factory = sqlite3.Row
placeholders = ','.join('?' for _ in request_ids)
rows = [dict(row) for row in connection.execute(
    f'SELECT RequestId, Stage, Provider, Model, Project, Slot, Status, InputTokens, OutputTokens, TotalTokens '
    f'FROM AiCalls WHERE RequestId IN ({placeholders}) ORDER BY StartedMs', request_ids)]
connection.close()
assert rows, 'No measured provider calls found for this run.'
assert all(row['Provider'].lower() == 'gemini' for row in rows), 'A non-Gemini provider was used.'
assert all(row['Status'] == 'succeeded' for row in rows), 'Provider calls include a failure.'
summary = {'configuredProvider': runtime['AiProvider']['Provider'], 'model': runtime['Gemini']['Model'],
           'measuredCalls': len(rows), 'requestIdsWithModelCall': len({row['RequestId'] for row in rows}),
           'stages': sorted({row['Stage'] for row in rows}), 'allSucceeded': True, 'calls': rows}
(evidence / 'real-provider-verification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({key: value for key, value in summary.items() if key != 'calls'}, ensure_ascii=False))
