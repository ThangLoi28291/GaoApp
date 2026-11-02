package vn.gaoapp.acbprobe;

import android.app.Notification;
import android.content.Context;
import android.content.ComponentName;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.Signature;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.service.notification.NotificationListenerService;
import android.service.notification.StatusBarNotification;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.UUID;

public final class AcbNotificationListener extends NotificationListenerService {
    private static volatile AcbNotificationListener active;
    private static long passiveRebindAt;
    private long sessionConnectedAt;
    private int sessionSequence;

    private static SharedPreferences lifecycle(Context context) { return context.getSharedPreferences("listener-lifecycle", Context.MODE_PRIVATE); }

    static JSONObject lifecycleEvidence(Context context) {
        SharedPreferences saved = lifecycle(context); JSONObject data = new JSONObject();
        try {
            data.put("connectSequence", saved.getInt("connectSequence", 0));
            data.put("lastConnectedAtEpochMs", saved.getLong("connectedAt", 0));
            data.put("restartStatus", saved.getString("restartStatus", "NOT_REQUESTED"));
            data.put("restartRequestedAtEpochMs", saved.getLong("restartAt", 0));
            data.put("unbindRequestedAtEpochMs", saved.getLong("unbindAt", 0));
            data.put("rebindRequestedAtEpochMs", saved.getLong("rebindAt", 0));
            data.put("restartConfirmedAtEpochMs", saved.getLong("confirmedAt", 0));
            data.put("lastCallbackAtEpochMs", saved.getLong("lastCallbackAt", 0));
            data.put("lastPassiveRebindAtEpochMs", saved.getLong("passiveRebindAt", 0));
        } catch (JSONException impossible) { throw new IllegalStateException(impossible); }
        return data;
    }

    static void restartAfterPairing(Context source) {
        Context context = source.getApplicationContext();
        new Handler(Looper.getMainLooper()).post(() -> {
            if (!MainActivity.accessEnabled(context)) { ProbeStore.listenerError = "Cần bật quyền đọc thông báo rồi thử kết nối lại."; return; }
            SharedPreferences saved = lifecycle(context);
            if ("PENDING".equals(saved.getString("restartStatus", ""))) return;
            long requested = System.currentTimeMillis();
            saved.edit().putString("restartStatus", "PENDING").putLong("restartAt", requested)
                .putLong("unbindAt", 0).putLong("rebindAt", 0).putLong("confirmedAt", 0)
                .putInt("beforeSequence", saved.getInt("connectSequence", 0)).commit();
            try {
                AcbNotificationListener previous = active;
                if (Build.VERSION.SDK_INT >= 34) requestUnbind(new ComponentName(context, AcbNotificationListener.class));
                else if (previous != null && ProbeStore.listenerConnected) previous.requestUnbind();
                saved.edit().putLong("unbindAt", System.currentTimeMillis()).commit();
                active = null; ProbeStore.listenerConnected = false;
                ProbeStore.listenerError = "Đang mở lại dịch vụ đọc sau khi ghép thiết bị.";
                new Handler(Looper.getMainLooper()).postDelayed(() -> requestFreshBinding(context, requested), 800);
                new Handler(Looper.getMainLooper()).postDelayed(() -> {
                    if (saved.getLong("restartAt", 0) == requested && "PENDING".equals(saved.getString("restartStatus", ""))) {
                        saved.edit().putString("restartStatus", "NOT_CONFIRMED").commit();
                        ProbeStore.listenerError = "Android chưa xác nhận kết nối mới. Giữ liên kết đồng hành, tắt rồi bật lại quyền truy cập thông báo của GaoApp.";
                    }
                }, 15000);
            } catch (RuntimeException denied) {
                saved.edit().putString("restartStatus", "NOT_CONFIRMED").commit();
                ProbeStore.listenerError = "Chưa mở lại được dịch vụ. Giữ liên kết đồng hành, tắt rồi bật lại quyền đọc thông báo của GaoApp.";
            }
        });
    }

    private static void requestFreshBinding(Context context, long requested) {
        SharedPreferences saved = lifecycle(context);
        if (saved.getLong("restartAt", 0) != requested || !MainActivity.accessEnabled(context)) return;
        try {
            saved.edit().putLong("rebindAt", System.currentTimeMillis()).commit();
            requestRebind(new ComponentName(context, AcbNotificationListener.class));
        } catch (RuntimeException denied) { ProbeStore.listenerError = "Android chưa cho mở lại dịch vụ đọc; kiểm tra quyền truy cập thông báo."; }
    }

    static void resumePendingRestart(Context source) {
        Context context = source.getApplicationContext(); SharedPreferences saved = lifecycle(context);
        if (!ProbeStore.listenerConnected && "PENDING".equals(saved.getString("restartStatus", ""))) {
            long requested = saved.getLong("restartAt", 0);
            if (System.currentTimeMillis() - requested > 15000) saved.edit().putString("restartStatus", "NOT_CONFIRMED").commit();
            requestFreshBinding(context, requested);
        }
    }

    /** Recover a known disconnected grant without disturbing a live companion listener. */
    static void ensureBinding(Context source) {
        Context context = source.getApplicationContext();
        if (active != null && ProbeStore.listenerConnected || !MainActivity.accessEnabled(context)
                || "PENDING".equals(lifecycle(context).getString("restartStatus", ""))) return;
        synchronized (AcbNotificationListener.class) {
            long now = android.os.SystemClock.elapsedRealtime();
            if (active != null && ProbeStore.listenerConnected || !RelayRecoveryPolicy.mayRebind(now, passiveRebindAt)) return;
            passiveRebindAt = now;
        }
        lifecycle(context).edit().putLong("passiveRebindAt", System.currentTimeMillis()).apply();
        try { requestRebind(new ComponentName(context, AcbNotificationListener.class)); }
        catch (RuntimeException denied) { ProbeStore.listenerError = "Android chưa cho nối lại dịch vụ đọc; kiểm tra quyền chạy nền."; }
    }

    @Override public void onListenerConnected() {
        active = this; ProbeStore.listenerConnected = true; ProbeStore.listenerError = "";
        WebBridge.start(this);
        SharedPreferences saved = lifecycle(this);
        sessionConnectedAt = System.currentTimeMillis(); sessionSequence = saved.getInt("connectSequence", 0) + 1;
        String status = saved.getString("restartStatus", "");
        boolean restarted = ("PENDING".equals(status) || "NOT_CONFIRMED".equals(status))
            && saved.getLong("rebindAt", 0) >= saved.getLong("restartAt", 0)
            && saved.getLong("restartAt", 0) > 0 && sessionSequence > saved.getInt("beforeSequence", 0);
        SharedPreferences.Editor change = saved.edit().putInt("connectSequence", sessionSequence).putLong("connectedAt", sessionConnectedAt);
        if (restarted) change.putString("restartStatus", "CONFIRMED").putLong("confirmedAt", sessionConnectedAt);
        change.commit();
        if (restarted) scanActive();
    }

    @Override public void onListenerDisconnected() {
        if (active == this) { active = null; ProbeStore.listenerConnected = false; }
        // Android may unbind the listener while idle. Ask to restore the existing
        // grant, without unbinding a live service or interrupting pairing refresh.
        ensureBinding(this);
    }

    @Override public void onDestroy() {
        if (active == this) { active = null; ProbeStore.listenerConnected = false; }
        super.onDestroy();
    }

    @Override public void onNotificationPosted(StatusBarNotification notification) {
        if (notification != null && TransferProbeParser.isAcbPackage(notification.getPackageName()))
            lifecycle(this).edit().putLong("lastCallbackAt", System.currentTimeMillis()).apply();
        capture(notification, "POSTED");
    }

    static boolean scanActive() {
        AcbNotificationListener listener = active;
        if (listener == null || !ProbeStore.listenerConnected) return false;
        try {
            StatusBarNotification[] items = listener.getActiveNotifications();
            if (items != null) for (StatusBarNotification item : items) listener.capture(item, "ACTIVE_SCAN");
            return true;
        } catch (SecurityException unavailable) {
            ProbeStore.listenerError = "Không đọc được thông báo đang hiện; kiểm tra lại quyền.";
            return false;
        }
    }

    private void capture(StatusBarNotification item, String trigger) {
        if (item == null || !TransferProbeParser.isAcbPackage(item.getPackageName())) return;
        final long observedAt = System.currentTimeMillis();
        final long listenerConnectedAt = sessionConnectedAt;
        final int listenerSession = sessionSequence;
        if ((item.getNotification().flags & Notification.FLAG_GROUP_SUMMARY) != 0) {
            ProbeStore.IO.execute(() -> ProbeStore.get(this).observed("groupSummariesIgnored", observedAt));
            return;
        }
        // Copy only documented text fields; never serialize arbitrary notification extras/Intents.
        final NotificationTextReader.Payload payload = NotificationTextReader.read(this, item.getNotification());
        final String scenario = getSharedPreferences("probe-settings", MODE_PRIVATE).getString("scenario", "Bình thường");
        final String notificationKey = item.getKey();
        final long postedAt = item.getPostTime();
        ProbeStore.IO.execute(() -> {
            try {
                ProbeStore store = ProbeStore.get(this);
                if (payload.containsSensitive) { store.observed("sensitiveIgnored", observedAt); return; }
                TransferProbeParser.Analysis analysis = TransferProbeParser.inspect(payload.combined);
                // A redacted bank notification can have only a generic title. Retain that absence
                // as diagnostic evidence without assuming it represents a credit.
                if ("UNRECOGNIZED".equals(analysis.kind) && payload.bodyEmpty) {
                    analysis = TransferProbeParser.inspect("Thông báo thay đổi số dư: Android không trả nội dung.");
                    analysis.warnings.add("Thông báo ACB chỉ có tiêu đề hoặc trống; chưa xác định được loại giao dịch.");
                }
                if (!analysis.shouldRetain()) {
                    store.observed("SENSITIVE_IGNORED".equals(analysis.kind) ? "sensitiveIgnored" : "otherIgnored", observedAt);
                    return;
                }
                JSONObject data = new JSONObject();
                data.put("kind", analysis.kind);
                data.put("amountVnd", analysis.amountVnd == null ? JSONObject.NULL : analysis.amountVnd);
                data.put("orderCode", nullable(analysis.orderCode));
                data.put("ftReference", nullable(analysis.ftReference));
                data.put("mgdReference", nullable(analysis.mgdReference));
                data.put("accountNumber", nullable(analysis.accountNumber));
                data.put("bankTimeText", analysis.bankTimeText);
                data.put("warnings", new JSONArray(analysis.warnings));
                if (payload.truncated) data.getJSONArray("warnings").put("Nội dung vượt giới hạn cục bộ; bản gốc có thể thiếu một phần.");
                if (payload.androidLimitSuspected) data.getJSONArray("warnings").put("Có trường dài đúng 1024 hoặc 5120 ký tự; Android có thể đã cắt nội dung trước khi app nhận. Chưa kết luận nội dung đầy đủ.");
                if (payload.redactionSuspected) data.getJSONArray("warnings").put("Android có thể đang che nội dung. Chỉ giữ dữ liệu được cấp, chưa đọc được nội dung đầy đủ.");
                if (payload.fieldErrors.length() > 0) data.getJSONArray("warnings").put("Có trường hoặc bố cục chưa đọc được. Xem fieldErrors trong các trường gốc.");
                JSONObject event = new JSONObject();
                event.put("captureId", UUID.randomUUID().toString());
                event.put("sourcePackage", TransferProbeParser.ACB_PACKAGE);
                event.put("bankApp", bankAppIdentity(this));
                event.put("companion", CompanionLink.evidence(this));
                event.put("notificationKeyHash", sha256(notificationKey));
                event.put("notificationPostedAtEpochMs", postedAt);
                event.put("firstObservedAtEpochMs", observedAt);
                event.put("listenerConnectedAtEpochMs", listenerConnectedAt);
                event.put("listenerConnectSequence", listenerSession);
                event.put("lastObservedAtEpochMs", observedAt);
                event.put("firstTrigger", trigger);
                event.put("lastTrigger", trigger);
                event.put("scenario", scenario);
                event.put("deliveryCount", 1);
                event.put("analysis", data);
                event.put("raw", payload.raw);
                event.put("textTruncated", payload.truncated);
                event.put("androidTextLimitSuspected", payload.androidLimitSuspected);
                String captureKey = sha256(notificationKey + "\n" + postedAt + "\n" + payload.combined);
                store.retain(captureKey, event);
                WebBridge.get(this).enqueue(captureKey, payload.combined, postedAt, payload.truncated || payload.androidLimitSuspected);
                ProbeStore.listenerError = "";
            } catch (Exception failure) {
                // No notification text or bank fields in system logs/error messages.
                ProbeStore.listenerError = "Chưa lưu được thông báo: " + failure.getClass().getSimpleName();
            }
        });
    }

    private static Object nullable(String value) { return value == null ? JSONObject.NULL : value; }

    @SuppressWarnings("deprecation")
    static JSONObject bankAppIdentity(Context context) {
        JSONObject result = new JSONObject();
        try {
            PackageManager pm = context.getPackageManager();
            int flags = Build.VERSION.SDK_INT >= 28 ? PackageManager.GET_SIGNING_CERTIFICATES : PackageManager.GET_SIGNATURES;
            PackageInfo info = pm.getPackageInfo(TransferProbeParser.ACB_PACKAGE, flags);
            result.put("installed", true);
            result.put("packageName", TransferProbeParser.ACB_PACKAGE);
            result.put("versionName", info.versionName);
            result.put("versionCode", Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode);
            Signature[] signatures = Build.VERSION.SDK_INT >= 28 && info.signingInfo != null
                ? info.signingInfo.getApkContentsSigners() : info.signatures;
            JSONArray certs = new JSONArray();
            if (signatures != null) for (Signature signature : signatures) certs.put(digest(signature.toByteArray()));
            result.put("signerSha256", certs);
            result.put("identityStatus", "PACKAGE_AND_CERTIFICATE_OBSERVED_NOT_BANK_VERIFIED");
        } catch (PackageManager.NameNotFoundException missing) {
            try { result.put("installed", false); result.put("packageName", TransferProbeParser.ACB_PACKAGE); }
            catch (JSONException impossible) { throw new IllegalStateException(impossible); }
        } catch (JSONException invalid) { throw new IllegalStateException(invalid); }
        return result;
    }

    static String sha256(String value) { return digest(value.getBytes(StandardCharsets.UTF_8)); }

    private static String digest(byte[] value) {
        try {
            byte[] bytes = MessageDigest.getInstance("SHA-256").digest(value);
            StringBuilder result = new StringBuilder();
            for (byte part : bytes) result.append(String.format(java.util.Locale.ROOT, "%02x", part & 0xff));
            return result.toString();
        } catch (java.security.NoSuchAlgorithmException impossible) { throw new IllegalStateException(impossible); }
    }

}
