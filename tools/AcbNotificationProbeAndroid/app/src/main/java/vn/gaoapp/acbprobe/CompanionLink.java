package vn.gaoapp.acbprobe;

import android.Manifest;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothSocket;
import android.companion.AssociationInfo;
import android.companion.CompanionDeviceManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import org.json.JSONObject;
import org.json.JSONException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

final class CompanionLink {
    private static final ScheduledExecutorService WORK = Executors.newScheduledThreadPool(2);
    private static volatile BluetoothSocket socket;
    static volatile boolean connected;
    static volatile String message = "Chưa kết nối máy tính.";
    private static volatile int generation;
    private CompanionLink() { }
    static SharedPreferences preferences(Context context) { return context.getSharedPreferences("companion-probe", Context.MODE_PRIVATE); }

    @SuppressWarnings("deprecation")
    static boolean associated(Context context, String address) {
        if (address == null || address.isEmpty()) return false;
        CompanionDeviceManager manager = context.getSystemService(CompanionDeviceManager.class);
        if (manager == null) return false;
        try {
            if (Build.VERSION.SDK_INT >= 33) {
                for (AssociationInfo info : manager.getMyAssociations()) {
                    if (!info.isSelfManaged() && info.getDeviceMacAddress() != null && address.equalsIgnoreCase(info.getDeviceMacAddress().toString())) return true;
                }
            } else for (String item : manager.getAssociations()) if (address.equalsIgnoreCase(item)) return true;
        } catch (RuntimeException ignored) { return false; }
        return false;
    }

    static JSONObject evidence(Context context) {
        JSONObject result = new JSONObject();
        SharedPreferences saved = preferences(context);
        try {
            String address = saved.getString("address", "");
            result.put("associationApproved", associated(context, address));
            result.put("deviceHandshakeVerified", saved.getBoolean("verified", false));
            result.put("linkConnected", connected);
            result.put("lastVerifiedAtEpochMs", saved.getLong("verifiedAt", 0));
            result.put("scope", "REAL_BLUETOOTH_DEVICE_LOCAL_HEALTH_CHECK_ONLY");
        } catch (JSONException impossible) { throw new IllegalStateException(impossible); }
        return result; // No Bluetooth address or pairing secret in the exported report.
    }

    static synchronized void disconnect() {
        generation++; connected = false;
        BluetoothSocket current = socket; socket = null;
        if (current != null) try { current.close(); } catch (java.io.IOException ignored) { }
        message = "Kết nối máy tính đã dừng.";
    }

    @SuppressWarnings("deprecation")
    static void remove(Context context, String address) {
        disconnect();
        CompanionDeviceManager manager = context.getSystemService(CompanionDeviceManager.class);
        if (manager == null) throw new IllegalStateException("Thiết bị không hỗ trợ ghép đồng hành.");
        if (Build.VERSION.SDK_INT >= 33) {
            for (AssociationInfo info : manager.getMyAssociations()) if (address.isEmpty() || info.getDeviceMacAddress() != null && address.equalsIgnoreCase(info.getDeviceMacAddress().toString())) manager.disassociate(info.getId());
        } else for (String item : manager.getAssociations()) if (address.isEmpty() || address.equalsIgnoreCase(item)) manager.disassociate(item);
        preferences(context).edit().clear().apply();
        message = "Đã hủy ghép thiết bị đồng hành của GaoApp.";
    }

    static void connect(Context source, String address, String pin, boolean newAssociation) {
        final Context context = source.getApplicationContext();
        disconnect(); final int attempt = generation;
        message = "Đang kết nối máy tính; xác nhận ghép Bluetooth trên cả hai thiết bị nếu được hỏi.";
        WORK.execute(() -> {
            BluetoothSocket candidate = null;
            boolean verified = false;
            try {
                if (!associated(context, address)) throw new java.io.IOException("NO_ASSOCIATION");
                BluetoothManager manager = context.getSystemService(BluetoothManager.class);
                if (manager == null || manager.getAdapter() == null || !manager.getAdapter().isEnabled()) throw new java.io.IOException("BLUETOOTH_OFF");
                if (Build.VERSION.SDK_INT >= 31 && context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != android.content.pm.PackageManager.PERMISSION_GRANTED) throw new SecurityException();
                BluetoothDevice device = manager.getAdapter().getRemoteDevice(address);
                candidate = device.createRfcommSocketToServiceRecord(UUID.fromString(CompanionProtocol.SERVICE_UUID));
                final BluetoothSocket timed = candidate;
                synchronized (CompanionLink.class) { if (attempt != generation) return; socket = candidate; }
                java.util.concurrent.ScheduledFuture<?> timeout = WORK.schedule(() -> { try { timed.close(); } catch (java.io.IOException ignored) { } }, 60, TimeUnit.SECONDS);
                try {
                    candidate.connect();
                    InputStream input = candidate.getInputStream(); OutputStream output = candidate.getOutputStream();
                    String nonce = UUID.randomUUID().toString().replace("-", "");
                    output.write(("HELLO GAOAPP1 " + pin + " " + nonce + "\n").getBytes(StandardCharsets.US_ASCII)); output.flush();
                    if (!CompanionProtocol.validHelloReply(readLine(input), nonce)) throw new java.io.IOException("HANDSHAKE_REJECTED");
                    synchronized (CompanionLink.class) {
                        if (attempt != generation) return;
                        preferences(context).edit().putString("address", address).putString("pin", pin).putBoolean("verified", true).putLong("verifiedAt", System.currentTimeMillis()).apply();
                        verified = true; connected = true;
                    }
                    message = "Đã ghép với máy tính và kiểm tra kết nối thật. Cần thử lại nội dung ACB.";
                    new Handler(Looper.getMainLooper()).post(() -> {
                        AcbNotificationListener.restartAfterPairing(context);
                    });
                    timeout.cancel(false);
                    while (attempt == generation) {
                        output.write("PING GAOAPP1\n".getBytes(StandardCharsets.US_ASCII)); output.flush();
                        java.util.concurrent.ScheduledFuture<?> heartbeat = WORK.schedule(() -> { try { timed.close(); } catch (java.io.IOException ignored) { } }, 15, TimeUnit.SECONDS);
                        try { if (!"PONG GAOAPP1".equals(readLine(input))) throw new java.io.IOException("HEARTBEAT_REJECTED"); }
                        finally { heartbeat.cancel(false); }
                        Thread.sleep(10000);
                    }
                } finally { timeout.cancel(false); }
            } catch (Exception unavailable) {
                if (attempt == generation) {
                    message = verified ? "Máy tính đã ngắt kết nối. Liên kết đồng hành vẫn còn; bấm Kết nối lại máy tính khi cần."
                        : "Chưa kiểm tra được kết nối máy tính (" + unavailable.getClass().getSimpleName() + "). Kiểm tra helper đang mở, mã ghép nối và xác nhận Bluetooth.";
                    if (newAssociation && !verified && associated(context, address)) {
                        try { remove(context, address); message = "Kết nối máy tính chưa được xác nhận; đã hủy liên kết vừa tạo. Kiểm tra mã ghép và mở lại helper rồi thử lại."; }
                        catch (RuntimeException cleanupFailed) { message = "Kết nối chưa được xác nhận. Bấm Hủy ghép thiết bị để xóa liên kết vừa tạo."; }
                    }
                }
            } finally {
                if (candidate != null) try { candidate.close(); } catch (java.io.IOException ignored) { }
                if (attempt == generation) { connected = false; socket = null; }
            }
        });
    }

    static String readLine(InputStream input) throws java.io.IOException {
        StringBuilder line = new StringBuilder();
        for (int i = 0; i < 512; i++) {
            int value = input.read(); if (value == -1) throw new java.io.EOFException();
            if (value == '\n') return line.toString();
            if (value < 32 || value > 126) throw new java.io.IOException("INVALID_FRAME");
            line.append((char) value);
        }
        throw new java.io.IOException("FRAME_TOO_LONG");
    }
}
