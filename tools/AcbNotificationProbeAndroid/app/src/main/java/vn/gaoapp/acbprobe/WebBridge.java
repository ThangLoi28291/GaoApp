package vn.gaoapp.acbprobe;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import org.json.JSONObject;
import java.net.URI;
import java.net.HttpURLConnection;
import java.nio.charset.StandardCharsets;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.ByteArrayOutputStream;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledThreadPoolExecutor;

/** Device-only upload queue; it cannot confirm orders and excludes OTP before persistence. */
final class WebBridge extends SQLiteOpenHelper {
    private static WebBridge instance;
    private static final ScheduledExecutorService NETWORK = Executors.newSingleThreadScheduledExecutor();
    private static final ScheduledThreadPoolExecutor DEADLINES = deadlineExecutor();
    private final ResilientRelayLoop worker;
    private final Context context;
    private final SharedPreferences settings;
    private static final int RECOVERY_JOB = 42017;
    private long nextHeartbeat;
    private long recoveryJobAt;
    private boolean recoveryJobRunning;
    private static ScheduledThreadPoolExecutor deadlineExecutor() {
        ScheduledThreadPoolExecutor timer = new ScheduledThreadPoolExecutor(1);
        timer.setRemoveOnCancelPolicy(true);
        return timer;
    }
    private WebBridge(Context context) {
        super(context, "web-notification-outbox.db", null, 1);
        this.context = context.getApplicationContext();
        settings = this.context.getSharedPreferences("web-notification-bridge", Context.MODE_PRIVATE);
        worker = new ResilientRelayLoop(NETWORK, this::isEnabled, this::pulse,
            () -> backgroundStatus("Tác vụ nền gặp lỗi; giữ hàng đợi và tự phục hồi."), 5000L);
    }
    static synchronized WebBridge get(Context context) {
        if (instance == null) instance = new WebBridge(context.getApplicationContext());
        return instance;
    }
    static void start(Context context) {
        WebBridge bridge = get(context);
        if (!bridge.isEnabled()) return;
        bridge.safeScheduleRecovery(); // Arm recovery before a possibly stalled network call.
        WebBridgeBackgroundService.ensureStarted(context);
        startWorker(context);
    }
    static void startWorker(Context context) {
        WebBridge bridge = get(context);
        if (!bridge.isEnabled()) return;
        // A listener/network/alarm callback already has a CPU opportunity. Renew
        // here, before queuing work, rather than relying only on that work to wake itself.
        WebBridgeBackgroundService.renewWakeLock();
        AcbNotificationListener.ensureBinding(context);
        bridge.worker.start();
    }
    static void recoveryWake(Context context, String source) {
        WebBridge bridge = get(context);
        if (!bridge.isEnabled()) return;
        bridge.settings.edit().putLong("recoveryWakeAt", System.currentTimeMillis()).putString("recoveryWakeSource", source).apply();
        start(context);
    }
    boolean isEnabled() { return settings.getBoolean("enabled", false) && settings.getInt("device", 0) > 0; }
    synchronized void backgroundStatus(String message) { settings.edit().putString("backgroundStatus", message).apply(); }
    synchronized void foregroundAvailable() { settings.edit().remove("backgroundStatus").apply(); }
    private void stopBackgroundSending() {
        worker.stop(); WebBridgeBackgroundService.stop(context);
        safeScheduleRecovery();
    }
    @Override public void onCreate(SQLiteDatabase db) {
        db.execSQL("CREATE TABLE outbox (id TEXT PRIMARY KEY, body TEXT NOT NULL, next_attempt INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0, blocked INTEGER NOT NULL DEFAULT 0)");
    }
    @Override public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) { }
    synchronized void configure(JSONObject config) throws Exception {
        if (config.optInt("schemaVersion") != 1) throw new IllegalArgumentException("Định dạng cấu hình chưa hỗ trợ.");
        String api = config.getString("apiUrl");
        int device = config.getInt("deviceId"); String token = config.getString("token");
        WebBridgeProtocol.validate(api, device, token);
        boolean same = api.equals(settings.getString("api", "")) && device == settings.getInt("device", 0);
        if (!same && count() > 0) throw new IllegalArgumentException("Hàng đợi cấu hình cũ chưa gửi hết. Gửi hết hoặc xóa hàng đợi trước khi đổi cửa hàng/tài khoản.");
        settings.edit().putString("api", api).putInt("device", device).putString("token", token).putString("name", config.optString("deviceName", "Điện thoại cửa hàng"))
            .putBoolean("enabled", true).putLong("since", same ? settings.getLong("since", System.currentTimeMillis()) : System.currentTimeMillis())
            .putInt("lastErrorCode", 0).putString("status", same ? "Đã cập nhật cấu hình. Tiếp tục gửi tin đang chờ." : "Đã nhập cấu hình. Chỉ gửi thông báo mới từ thời điểm này.").commit();
        if (same) getWritableDatabase().execSQL("UPDATE outbox SET blocked=0, next_attempt=0");
        nextHeartbeat = 0;
        start(context);
    }
    synchronized void enabled(boolean enabled) {
        if (settings.getInt("device", 0) <= 0) return;
        settings.edit().putBoolean("enabled", enabled).putString("status", enabled ? "Đang chờ gửi thông báo mới." : "Đã ngừng gửi web.").commit();
        if (enabled) { getWritableDatabase().execSQL("UPDATE outbox SET blocked=0, next_attempt=0"); nextHeartbeat=0; start(context); }
        else stopBackgroundSending();
    }
    synchronized String status() {
        return settings.getString("name", "Chưa nhập cấu hình web") + " · Thiết bị #" + settings.getInt("device", 0) + "\n" + settings.getString("api", "") + "\n" + (settings.getBoolean("enabled", false) ? "Gửi web đang bật" : "Gửi web đang tắt")
            + " · " + count() + " tin trong hàng đợi\n" + settings.getString("status", "")
            + "\nDịch vụ nền: " + (WebBridgeBackgroundService.isRunning() ? "đang chạy" : "chưa chạy")
            + "\nPin: " + (BackgroundReadiness.batteryExempt(context) ? "đã cho phép chạy khi khóa máy" : "cần cho phép chạy khi khóa máy")
            + (BackgroundReadiness.notificationsAllowed(context) ? "" : "\nCần cho phép thông báo trạng thái chạy nền.")
            + (settings.getString("backgroundStatus", "").isEmpty() ? "" : "\n" + settings.getString("backgroundStatus", ""));
    }
    synchronized JSONObject evidence() throws org.json.JSONException {
        JSONObject value = new JSONObject();
        value.put("configured", settings.getInt("device", 0) > 0);
        value.put("uploadEnabled", settings.getBoolean("enabled", false));
        value.put("foregroundServiceRunning", WebBridgeBackgroundService.isRunning());
        value.put("batteryOptimizationExempt", BackgroundReadiness.batteryExempt(context));
        value.put("notificationPermissionGranted", BackgroundReadiness.notificationsAllowed(context));
        value.put("backgroundStatus", settings.getString("backgroundStatus", ""));
        value.put("lastHeartbeatAtEpochMs", settings.getLong("heartbeatAt", 0));
        value.put("lastHeartbeatAttemptAtEpochMs", settings.getLong("heartbeatAttemptAt", 0));
        value.put("lastHeartbeatFinishedAtEpochMs", settings.getLong("heartbeatFinishedAt", 0));
        value.put("lastHeartbeatHttpStatus", settings.getInt("heartbeatHttpStatus", 0));
        value.put("lastRelayCycleStartedAtEpochMs", settings.getLong("cycleStartedAt", 0));
        value.put("lastRelayCycleFinishedAtEpochMs", settings.getLong("cycleFinishedAt", 0));
        value.put("lastRecoveryJobAtEpochMs", settings.getLong("recoveryJobAt", 0));
        value.put("lastRecoveryJobStopReason", settings.getInt("recoveryJobStopReason", 0));
        value.put("lastRecoveryWakeAtEpochMs", settings.getLong("recoveryWakeAt", 0));
        value.put("lastRecoveryWakeSource", settings.getString("recoveryWakeSource", ""));
        value.put("lastNetworkDeadlineAtEpochMs", settings.getLong("networkDeadlineAt", 0));
        value.put("queuedCount", count());
        value.put("lastAcknowledgedAtEpochMs", settings.getLong("ackAt", 0));
        value.put("lastReceiptId", settings.getInt("receipt", 0));
        value.put("status", settings.getString("status", ""));
        return value; // Never export API credentials or configuration files.
    }
    private int count() { try (Cursor c = getReadableDatabase().rawQuery("SELECT COUNT(*) FROM outbox", null)) { return c.moveToFirst() ? c.getInt(0) : 0; } }
    synchronized void clearQueue() { getWritableDatabase().delete("outbox", null, null); }
    synchronized void enqueue(String key, String content, long postedAt, boolean truncated) {
        if (!settings.getBoolean("enabled", false) || !WebBridgeProtocol.eligible(content, postedAt, settings.getLong("since", Long.MAX_VALUE), truncated)) return;
        try {
            if (count() >= 1000) { settings.edit().putString("status", "Hàng đợi đã đầy. Kiểm tra kết nối web; chưa gửi được tin mới.").apply(); return; }
            JSONObject body = new JSONObject(); body.put("sourcePackage", TransferProbeParser.ACB_PACKAGE); body.put("content", content); body.put("postedAtEpochMs", postedAt);
            ContentValues v = new ContentValues(); v.put("id", key); v.put("body", body.toString());
            getWritableDatabase().insertWithOnConflict("outbox", null, v, SQLiteDatabase.CONFLICT_IGNORE); start(context);
        } catch (Exception failure) { settings.edit().putString("status", "Chưa lưu được hàng đợi gửi web.").apply(); }
    }
    private void flush() {
        String api, token; int device;
        synchronized (this) {
            if (!settings.getBoolean("enabled", false)) return;
            api = settings.getString("api", ""); token = settings.getString("token", ""); device = settings.getInt("device", 0);
        }
        for (int batch = 0; batch < 2; batch++) {
            String id, body; int attempts;
            synchronized (this) {
                if (!sameConfiguration(api, device, token)) return;
                try (Cursor c = getReadableDatabase().rawQuery("SELECT id, body, attempts FROM outbox WHERE blocked = 0 AND next_attempt <= ? ORDER BY rowid LIMIT 1", new String[]{Long.toString(System.currentTimeMillis())})) {
                    if (!c.moveToFirst()) return;
                    id = c.getString(0); body = c.getString(1); attempts = c.getInt(2);
                }
            }
            HttpURLConnection connection = null;
            RelayRequestDeadline deadline = null;
            try {
                connection = (HttpURLConnection) new URI(api).toURL().openConnection(); connection.setInstanceFollowRedirects(false);
                deadline = new RelayRequestDeadline(connection, DEADLINES, RelayRecoveryPolicy.NETWORK_DEADLINE_MILLIS);
                connection.setConnectTimeout(8000); connection.setReadTimeout(8000); connection.setRequestMethod("POST"); connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                connection.setRequestProperty("X-GaoApp-Notification-Device", device + ":" + token);
                byte[] bytes = body.getBytes(StandardCharsets.UTF_8); connection.setFixedLengthStreamingMode(bytes.length);
                try (OutputStream out = connection.getOutputStream()) { out.write(bytes); }
                int code = connection.getResponseCode();
                if (code >= 200 && code < 300) {
                    byte[] ack; try (InputStream input = connection.getInputStream()) { ack = boundedRead(input, 4096); }
                    JSONObject response = new JSONObject(new String(ack, StandardCharsets.UTF_8));
                    if (!response.optBoolean("accepted")) throw new IllegalStateException("Missing acknowledgement");
                    synchronized (this) {
                        if (!sameConfiguration(api, device, token)) return;
                        getWritableDatabase().delete("outbox", "id = ?", new String[]{id});
                        settings.edit().putInt("lastErrorCode", 0).putLong("ackAt", System.currentTimeMillis()).putInt("receipt", response.optInt("receiptId", 0))
                            .putString("status", response.optInt("receiptId", 0) > 0 ? "Máy chủ đã nhận thông báo." : "Máy chủ đã nhận tin nhưng chưa nhận diện được giao dịch; kiểm tra thủ công.").apply();
                    }
                } else { retry(id, attempts, code, api, device, token,
                    code == 429 || code == 503 ? WebBridgeProtocol.retryAfterMillis(connection.getHeaderField("Retry-After")) : 0); return; }
            } catch (Exception failure) { retry(id, attempts, 0, api, device, token); return; }
            finally {
                if (deadline != null) {
                    if (deadline.expired()) settings.edit().putLong("networkDeadlineAt", System.currentTimeMillis()).apply();
                    deadline.close();
                }
                if (connection != null) connection.disconnect();
            }
        }
    }
    private synchronized boolean sameConfiguration(String api, int device, String token) {
        return settings.getBoolean("enabled", false) && WebBridgeProtocol.sameConfiguration(api, device, token,
            settings.getString("api", ""), settings.getInt("device", 0), settings.getString("token", ""));
    }
    private synchronized void retry(String id, int attempts, int code, String api, int device, String token) {
        retry(id, attempts, code, api, device, token, 0);
    }
    private synchronized void retry(String id, int attempts, int code, String api, int device, String token, long retryAfter) {
        if (!sameConfiguration(api, device, token)) return;
        ContentValues v = new ContentValues(); v.put("attempts", attempts + 1); v.put("next_attempt", System.currentTimeMillis() + Math.max(WebBridgeProtocol.retryDelayMillis(attempts), retryAfter));
        if (code >= 400 && code < 500 && code != 408 && code != 429) v.put("blocked", 1);
        getWritableDatabase().update("outbox", v, "id = ?", new String[]{id});
        settings.edit().putInt("lastErrorCode", code).putString("status", WebBridgeProtocol.failureMessage(code, settings.getInt("device", 0))).apply();
        if (code == 401 || code == 403) { settings.edit().putBoolean("enabled", false).commit(); stopBackgroundSending(); }
    }
    private void networkCycle() {
        if (!isEnabled()) return;
        settings.edit().putLong("cycleStartedAt", System.currentTimeMillis()).apply();
        try {
            safeScheduleRecovery();
            WebBridgeBackgroundService.renewWakeLock();
            AcbNotificationListener.ensureBinding(context);
            // A full outbox cannot starve the contact signal for several minutes.
            heartbeat(); flush();
        } catch (RuntimeException failure) {
            // ScheduledExecutor suppresses all future ticks when a task throws.
            // Keep the relay alive without logging bank content or credentials.
            backgroundStatus("Tác vụ gửi gặp lỗi; giữ hàng đợi và tự phục hồi.");
        } finally { settings.edit().putLong("cycleFinishedAt", System.currentTimeMillis()).apply(); }
    }
    private void pulse() { try { networkCycle(); } finally { safeScheduleRecovery(); } }
    static void recover(Context context, Runnable finished) {
        WebBridge bridge = get(context);
        synchronized (bridge) { bridge.recoveryJobAt = 0; bridge.recoveryJobRunning = true; }
        bridge.settings.edit().putLong("recoveryJobAt", System.currentTimeMillis()).apply();
        NETWORK.execute(() -> {
            try { bridge.networkCycle(); }
            finally {
                try { finished.run(); }
                finally {
                    synchronized (bridge) { bridge.recoveryJobRunning = false; }
                    bridge.safeScheduleRecovery();
                }
            }
        });
        // This can be denied when Android limits background starts. The job's
        // own network attempt still runs and the activity explains recovery.
        if (bridge.isEnabled()) WebBridgeBackgroundService.ensureStarted(context);
    }
    private void safeScheduleRecovery() {
        try { WebBridgeRecoveryReceiver.schedule(context, isEnabled()); }
        catch (RuntimeException failure) { backgroundStatus("Android chưa xếp được lịch đánh thức dự phòng; kiểm tra quyền chạy nền."); }
        try { scheduleRecovery(); }
        catch (RuntimeException failure) { backgroundStatus("Android chưa xếp được tác vụ dự phòng; kiểm tra quyền chạy nền."); }
    }
    private synchronized void scheduleRecovery() {
        android.app.job.JobScheduler scheduler = (android.app.job.JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (scheduler == null) return;
        if (!isEnabled()) { scheduler.cancel(RECOVERY_JOB); recoveryJobAt = 0; return; }
        if (recoveryJobRunning) return; // Do not replace/cancel the job executing this recovery cycle.
        long nextAttempt = Long.MAX_VALUE;
        try (Cursor c = getReadableDatabase().rawQuery("SELECT MIN(next_attempt) FROM outbox WHERE blocked=0", null)) {
            if (c.moveToFirst() && !c.isNull(0)) nextAttempt = c.getLong(0);
        }
        long elapsed = android.os.SystemClock.elapsedRealtime();
        long requested = RelayRecoveryPolicy.jobDeadline(elapsed, System.currentTimeMillis(), nextAttempt);
        if (scheduler.getPendingJob(RECOVERY_JOB) != null && !RelayRecoveryPolicy.replaceJob(recoveryJobAt, requested)) return;
        int result = scheduler.schedule(new android.app.job.JobInfo.Builder(RECOVERY_JOB, new android.content.ComponentName(context, WebBridgeJob.class))
            .setRequiredNetworkType(android.app.job.JobInfo.NETWORK_TYPE_ANY).setMinimumLatency(requested - elapsed).setPersisted(true).build());
        if (result == android.app.job.JobScheduler.RESULT_SUCCESS) recoveryJobAt = requested;
    }
    void recoveryJobStopped(int reason) { settings.edit().putInt("recoveryJobStopReason", reason).apply(); }
    private void heartbeat() {
        String api, token; int device, queued, blocked, error;
        synchronized (this) {
            if (!settings.getBoolean("enabled", false) || android.os.SystemClock.elapsedRealtime() < nextHeartbeat) return;
            nextHeartbeat = android.os.SystemClock.elapsedRealtime() + 30000L;
            settings.edit().putLong("heartbeatAttemptAt", System.currentTimeMillis()).putInt("heartbeatHttpStatus", 0).apply();
            api = settings.getString("api", ""); token = settings.getString("token", ""); device = settings.getInt("device", 0);
            queued = count(); error = settings.getInt("lastErrorCode", 0);
            try (Cursor c = getReadableDatabase().rawQuery("SELECT COUNT(*) FROM outbox WHERE blocked=1", null)) { blocked = c.moveToFirst() ? c.getInt(0) : 0; }
        }
        HttpURLConnection connection = null;
        RelayRequestDeadline deadline = null;
        try {
            connection = (HttpURLConnection) new URI(api + "/heartbeat").toURL().openConnection();
            deadline = new RelayRequestDeadline(connection, DEADLINES, RelayRecoveryPolicy.NETWORK_DEADLINE_MILLIS);
            connection.setInstanceFollowRedirects(false); connection.setConnectTimeout(8000); connection.setReadTimeout(8000);
            connection.setRequestMethod("POST"); connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setRequestProperty("X-GaoApp-Notification-Device", device + ":" + token);
            JSONObject status = new JSONObject(); status.put("queuedCount", queued); status.put("blockedCount", blocked); status.put("lastErrorCode", error);
            byte[] bytes = status.toString().getBytes(StandardCharsets.UTF_8); connection.setFixedLengthStreamingMode(bytes.length);
            try (OutputStream out = connection.getOutputStream()) { out.write(bytes); }
            int code = connection.getResponseCode();
            settings.edit().putInt("heartbeatHttpStatus", code).apply();
            if (code >= 200 && code < 300) {
                try (InputStream input = connection.getInputStream()) { boundedRead(input, 4096); }
                synchronized (this) { if (sameConfiguration(api, device, token)) settings.edit().putLong("heartbeatAt", System.currentTimeMillis()).apply(); }
            }
            else if (code == 401 || code == 403) synchronized (this) {
                if (sameConfiguration(api, device, token)) {
                    settings.edit().putBoolean("enabled", false).putInt("lastErrorCode", code)
                        .putString("status", WebBridgeProtocol.failureMessage(code, device)).commit();
                    stopBackgroundSending();
                }
            }
            // A health response never acknowledges or removes a payment notification.
        } catch (Exception ignored) { /* Durable outbox is retained until its own upload ACK. */ }
        finally {
            if (deadline != null) {
                if (deadline.expired()) settings.edit().putLong("networkDeadlineAt", System.currentTimeMillis()).apply();
                deadline.close();
            }
            settings.edit().putLong("heartbeatFinishedAt", System.currentTimeMillis()).apply();
            if (connection != null) connection.disconnect();
        }
    }
    static byte[] boundedRead(InputStream input, int limit) throws Exception {
        ByteArrayOutputStream out = new ByteArrayOutputStream(); byte[] buffer = new byte[1024]; int size;
        while ((size = input.read(buffer)) != -1) { if (out.size() + size > limit) throw new IllegalArgumentException("File quá lớn."); out.write(buffer, 0, size); }
        return out.toByteArray();
    }
}
