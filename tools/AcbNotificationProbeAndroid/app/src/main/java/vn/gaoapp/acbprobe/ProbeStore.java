package vn.gaoapp.acbprobe;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.os.Build;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

final class ProbeStore extends SQLiteOpenHelper {
    static final int RETENTION = 200;
    static final ExecutorService IO = Executors.newSingleThreadExecutor();
    static volatile boolean listenerConnected;
    static volatile String listenerError = "";
    private static ProbeStore instance;
    private volatile long revision;

    static synchronized ProbeStore get(Context context) {
        if (instance == null) instance = new ProbeStore(context.getApplicationContext());
        return instance;
    }

    private ProbeStore(Context context) { super(context, "acb-probe.db", null, 1); }

    @Override public void onCreate(SQLiteDatabase db) {
        db.execSQL("CREATE TABLE events (id INTEGER PRIMARY KEY AUTOINCREMENT, fingerprint TEXT NOT NULL UNIQUE, payload TEXT NOT NULL, last_observed INTEGER NOT NULL)");
        db.execSQL("CREATE TABLE metrics (name TEXT PRIMARY KEY, value INTEGER NOT NULL)");
    }

    @Override public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
        throw new IllegalStateException("Chưa có phiên bản nâng cấp cơ sở dữ liệu thử nghiệm.");
    }

    long revision() { return revision; }

    void observed(String category, long when) {
        SQLiteDatabase db = getWritableDatabase();
        db.beginTransaction();
        try {
            increment(db, "acbCallbacks");
            increment(db, category);
            setMetric(db, "lastAcbObserved", when);
            db.setTransactionSuccessful();
        } finally { db.endTransaction(); revision++; }
    }

    void retain(String fingerprint, JSONObject event) throws JSONException {
        SQLiteDatabase db = getWritableDatabase();
        db.beginTransaction();
        try {
            long when = event.getLong("lastObservedAtEpochMs");
            increment(db, "acbCallbacks");
            increment(db, "retainedCallbacks");
            if ("UNRECOGNIZED".equals(event.getJSONObject("analysis").optString("kind"))) increment(db, "unrecognizedCallbacks");
            if ("REDACTED".equals(event.getJSONObject("analysis").optString("kind"))) increment(db, "redactedCallbacks");
            setMetric(db, "lastAcbObserved", when);
            ContentValues values = new ContentValues();
            try (Cursor cursor = db.query("events", new String[]{"payload"}, "fingerprint=?", new String[]{fingerprint}, null, null, null)) {
                if (cursor.moveToFirst()) {
                    JSONObject saved = new JSONObject(cursor.getString(0));
                    saved.put("deliveryCount", saved.optInt("deliveryCount", 1) + 1);
                    saved.put("lastObservedAtEpochMs", when);
                    saved.put("lastTrigger", event.optString("lastTrigger"));
                    // A rescan after an app update can improve diagnostics for the same payload.
                    // Keep the first-observed metadata and delivery count, refresh extraction only.
                    for (String field : new String[]{"analysis", "raw", "textTruncated", "androidTextLimitSuspected", "companion", "listenerConnectedAtEpochMs", "listenerConnectSequence"}) {
                        if (event.has(field)) saved.put(field, event.get(field));
                    }
                    values.put("payload", saved.toString());
                    values.put("last_observed", when);
                    db.update("events", values, "fingerprint=?", new String[]{fingerprint});
                } else {
                    values.put("fingerprint", fingerprint);
                    values.put("payload", event.toString());
                    values.put("last_observed", when);
                    db.insertOrThrow("events", null, values);
                    increment(db, "uniqueCaptured");
                }
            }
            // Bounded local evidence, not a financial ledger. Record how many rows were evicted.
            int evicted = db.delete("events", "id NOT IN (SELECT id FROM events ORDER BY last_observed DESC, id DESC LIMIT " + RETENTION + ")", null);
            for (int i = 0; i < evicted; i++) increment(db, "evicted");
            db.setTransactionSuccessful();
        } finally { db.endTransaction(); revision++; }
    }

    private void increment(SQLiteDatabase db, String name) {
        db.execSQL("INSERT OR IGNORE INTO metrics(name,value) VALUES (?,0)", new Object[]{name});
        db.execSQL("UPDATE metrics SET value=value+1 WHERE name=?", new Object[]{name});
    }

    private void setMetric(SQLiteDatabase db, String name, long value) {
        ContentValues values = new ContentValues();
        values.put("name", name); values.put("value", value);
        db.insertWithOnConflict("metrics", null, values, SQLiteDatabase.CONFLICT_REPLACE);
    }

    JSONObject metrics() {
        JSONObject metrics = new JSONObject();
        try (Cursor cursor = getReadableDatabase().query("metrics", new String[]{"name", "value"}, null, null, null, null, null)) {
            while (cursor.moveToNext()) metrics.put(cursor.getString(0), cursor.getLong(1));
        } catch (JSONException impossible) { throw new IllegalStateException(impossible); }
        return metrics;
    }

    List<JSONObject> recent(int limit) {
        List<JSONObject> result = new ArrayList<>();
        try (Cursor cursor = getReadableDatabase().query("events", new String[]{"payload"}, null, null, null, null, "last_observed DESC, id DESC", Integer.toString(limit))) {
            while (cursor.moveToNext()) result.add(currentAnalysis(new JSONObject(cursor.getString(0))));
        } catch (JSONException invalid) { listenerError = "Một bản ghi cục bộ không đọc được."; }
        return result;
    }

    private JSONObject currentAnalysis(JSONObject event) throws JSONException {
        JSONObject raw = event.optJSONObject("raw");
        if (raw == null || raw.optString("combinedText").isEmpty()) return event;
        // Re-read retained evidence for display/export after an upgrade only.
        // Never write the row again, alter observation times or enqueue old money.
        JSONObject captured = event.optJSONObject("analysis");
        if (!event.has("analysisKindAtCapture") && captured != null)
            event.put("analysisKindAtCapture", captured.optString("kind"));
        TransferProbeParser.Analysis parsed = TransferProbeParser.inspect(raw.optBoolean("redactionSuspected")
            ? "Đã ẩn nội dung thông báo nhạy cảm" : raw.optString("combinedText"));
        JSONObject analysis = new JSONObject();
        analysis.put("kind", parsed.kind);
        analysis.put("amountVnd", parsed.amountVnd == null ? JSONObject.NULL : parsed.amountVnd);
        analysis.put("orderCode", parsed.orderCode == null ? JSONObject.NULL : parsed.orderCode);
        analysis.put("ftReference", parsed.ftReference == null ? JSONObject.NULL : parsed.ftReference);
        analysis.put("mgdReference", parsed.mgdReference == null ? JSONObject.NULL : parsed.mgdReference);
        analysis.put("accountNumber", parsed.accountNumber == null ? JSONObject.NULL : parsed.accountNumber);
        analysis.put("bankTimeText", parsed.bankTimeText);
        JSONArray warnings = new JSONArray(parsed.warnings);
        if (event.optBoolean("textTruncated")) warnings.put("Nội dung vượt giới hạn cục bộ; bản gốc có thể thiếu một phần.");
        if (event.optBoolean("androidTextLimitSuspected")) warnings.put("Có trường dài đúng 1024 hoặc 5120 ký tự; Android có thể đã cắt nội dung trước khi app nhận. Chưa kết luận nội dung đầy đủ.");
        if (raw.optBoolean("redactionSuspected")) warnings.put("Android có thể đang che nội dung. Chỉ giữ dữ liệu được cấp, chưa đọc được nội dung đầy đủ.");
        JSONArray fieldErrors = raw.optJSONArray("fieldErrors");
        if (fieldErrors != null && fieldErrors.length() > 0) warnings.put("Có trường hoặc bố cục chưa đọc được. Xem fieldErrors trong các trường gốc.");
        analysis.put("warnings", warnings);
        event.put("analysis", analysis);
        event.put("analysisRecomputed", true);
        return event;
    }

    void clear() {
        SQLiteDatabase db = getWritableDatabase();
        db.beginTransaction();
        try { db.delete("events", null, null); db.delete("metrics", null, null); db.setTransactionSuccessful(); }
        finally { db.endTransaction(); revision++; }
    }

    JSONObject report(Context context, boolean includeRaw) throws JSONException {
        JSONObject report = new JSONObject();
        report.put("schemaVersion", 1);
        report.put("appVersion", AppVersion.name(context));
        report.put("analysisSource", "CURRENT_PARSER_FROM_RETAINED_RAW_WHEN_AVAILABLE");
        report.put("purpose", "NOTIFICATION_OBSERVATION_WITH_OPTIONAL_WEB_BRIDGE");
        report.put("generatedAtEpochMs", System.currentTimeMillis());
        report.put("manufacturer", Build.MANUFACTURER);
        report.put("model", Build.MODEL);
        report.put("androidVersion", Build.VERSION.RELEASE);
        report.put("sdkInt", Build.VERSION.SDK_INT);
        report.put("notificationAccessEnabled", MainActivity.accessEnabled(context));
        report.put("listenerConnected", listenerConnected);
        report.put("listenerError", listenerError);
        report.put("listenerLifecycle", AcbNotificationListener.lifecycleEvidence(context));
        report.put("acbApp", AcbNotificationListener.bankAppIdentity(context));
        report.put("companion", CompanionLink.evidence(context));
        report.put("webBridge", WebBridge.get(context).evidence());
        report.put("rawIncluded", includeRaw);
        report.put("retentionLimit", RETENTION);
        report.put("metrics", metrics());
        report.put("notice", "Dữ liệu do Android cung cấp, chưa được xác minh bằng API ngân hàng. Giờ nhận và giờ đăng thông báo không thay thế giờ giao dịch. Không suy luận tài khoản khi nội dung không có tài khoản. Bản ghi trùng thông báo không phải quy tắc chống trùng khoản thu.");
        JSONArray events = new JSONArray();
        for (JSONObject saved : recent(RETENTION)) {
            JSONObject event = new JSONObject(saved.toString());
            if (!includeRaw) {
                event.remove("raw");
                JSONObject analysis = event.getJSONObject("analysis");
                String account = analysis.isNull("accountNumber") ? "" : analysis.optString("accountNumber", "");
                if (!account.isEmpty()) analysis.put("accountNumber", "***" + account.substring(Math.max(0, account.length() - 4)));
            }
            events.put(event);
        }
        report.put("events", events);
        return report;
    }
}
