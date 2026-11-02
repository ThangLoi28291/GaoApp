"""Inspect only the dedicated emulator's local, anonymized diagnostic database."""
import argparse
import json
import sqlite3
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("database", type=Path)
parser.add_argument("--report", required=True, type=Path)
parser.add_argument("--before", type=Path)
args = parser.parse_args()

with sqlite3.connect(f"file:{args.database.as_posix()}?mode=ro", uri=True) as db:
    events = [json.loads(row[0]) for row in db.execute("select payload from events order by id")]
    metrics = dict(db.execute("select name,value from metrics"))

checks = []


def check(name, value):
    checks.append({"name": name, "passed": bool(value)})
    if not value:
        raise AssertionError(name)


analyses = [event["analysis"] for event in events]
check("merchant credit parsed through actual NotificationListener", any(a["amountVnd"] == 75000 and a["orderCode"] == "POS-1838116" and a["mgdReference"] == "30989" for a in analyses))
check("account-less merchant notification keeps account unknown", any(a["amountVnd"] == 75000 and a["accountNumber"] is None for a in analyses))
check("balance notification reads account but does not invent an order", any(a["amountVnd"] == 180000 and a["accountNumber"] == "12345678" and a["orderCode"] is None for a in analyses))
check("debit remains a debit", any(a["kind"] == "DEBIT" and a["amountVnd"] == -180000 for a in analyses))
check("multiple payments in one notification remain ambiguous", any(a["kind"] == "INCOMPLETE" and a["amountVnd"] is None and "Có nhiều số tiền" in str(a["warnings"]) for a in analyses))
check("empty/redacted body captured as incomplete evidence", any(a["kind"] == "INCOMPLETE" and "Android không trả" in str(a["warnings"]) or "chỉ có tiêu đề" in str(a["warnings"]) for a in analyses))
check("OTP content never persisted", all("safekey" not in json.dumps(e).lower() and "mã xác thực otp" not in json.dumps(e, ensure_ascii=False).lower() for e in events))
check("OTP ignored; unknown bank notification retained for diagnosis", metrics.get("sensitiveIgnored", 0) >= 2 and metrics.get("unrecognizedCallbacks", 0) >= 1)
check("generic ACB text retained without claiming a credit", any(a["kind"] == "UNRECOGNIZED" and a["amountVnd"] is None for a in analyses))
check("8000 VND parsed from custom notification layout", any(a["amountVnd"] == 8000 and a["orderCode"] == "POS-1838200" and a["mgdReference"] == "40123" for a in analyses))
check("custom layout evidence retained", any(e["analysis"]["amountVnd"] == 8000 and e["raw"].get("remoteViewTexts") for e in events))
check("messaging text parsed when standard text is generic", any(a["amountVnd"] == 8001 and a["orderCode"] == "POS-1838201" for a in analyses))
check("ticker text parsed when standard text is generic", any(a["amountVnd"] == 8002 and a["orderCode"] == "POS-1838202" for a in analyses))
check("explicit redaction does not trigger layout text recovery", any(e["raw"].get("redactionSuspected") and e["analysis"]["amountVnd"] is None and not e["raw"].get("remoteViewTexts") for e in events))
check("real-device Vietnamese redaction phrase diagnosed", any(e["analysis"]["kind"] == "REDACTED" and e["raw"].get("redactionSuspected") and "Đã ẩn nội dung thông báo nhạy cảm" in e["raw"].get("combinedText", "") for e in events))
check("redacted notifications never supply financial fields", all(all(e["analysis"][field] is None for field in ["amountVnd", "orderCode", "ftReference", "mgdReference", "accountNumber"]) for e in events if e["analysis"]["kind"] == "REDACTED") and metrics.get("redactedCallbacks", 0) >= 1)
check("group summaries ignored", metrics.get("groupSummariesIgnored", 0) >= 1)
check("all five burst notifications retained", all(any(a["orderCode"] == f"POS-{2000000+i}" and a["amountVnd"] == 75000+i for a in analyses) for i in range(5)))
check("long text is read from Android's bigText field", any(len(e["raw"].get("android.bigText", "")) >= 1024 for e in events))
check("Android-side or local text limit explicitly diagnosed", any(e["textTruncated"] or e.get("androidTextLimitSuspected") for e in events))
check("notification received with screen locked", any(a["orderCode"] == "POS-3000001" and a["amountVnd"] == 75001 for a in analyses))
check("local capture works with emulator network off", any(a["orderCode"] == "POS-3000003" and a["amountVnd"] == 75003 for a in analyses))
check("source certificates observed, never declared bank-verified", all(e["bankApp"]["identityStatus"] == "PACKAGE_AND_CERTIFICATE_OBSERVED_NOT_BANK_VERIFIED" and len(e["bankApp"]["signerSha256"][0]) == 64 for e in events))
check("bank time and Android timestamps remain separate", all("notificationPostedAtEpochMs" in e and "firstObservedAtEpochMs" in e and "bankTimeText" in e["analysis"] for e in events))

if args.before:
    with sqlite3.connect(f"file:{args.before.as_posix()}?mode=ro", uri=True) as before:
        previous_ids = {json.loads(row[0])["captureId"] for row in before.execute("select payload from events")}
    check("existing evidence survives emulator restart", previous_ids.issubset({e["captureId"] for e in events}))
    check("listener resumes capture after emulator restart", any(a["orderCode"] == "POS-3000002" and a["amountVnd"] == 75002 for a in analyses))

args.report.write_text(json.dumps({"result": "passed", "checks": checks, "eventCount": len(events), "metrics": metrics,
    "scope": "Dedicated emulator and a clearly labeled fake notification emitter. No real bank login, bank transaction or web payment confirmation."}, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"PASS: {len(checks)} emulator checks; {len(events)} retained sample notifications")
