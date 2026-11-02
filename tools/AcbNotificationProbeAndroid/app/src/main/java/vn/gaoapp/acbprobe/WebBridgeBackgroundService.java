package vn.gaoapp.acbprobe;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.BroadcastReceiver;
import android.content.pm.ServiceInfo;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.os.SystemClock;

/** User-enabled, visible notification relay. Upload credentials and bank data never appear here. */
public final class WebBridgeBackgroundService extends Service {
    private static final String CHANNEL = "gaoapp-web-relay";
    private static final String STOP = "vn.gaoapp.acbprobe.STOP_WEB_RELAY";
    private static final int NOTIFICATION_ID = 42018;
    private static volatile boolean running;
    private static volatile WebBridgeBackgroundService active;
    private static long startRequestedAt;
    private final Object wakeLockGuard = new Object();
    private PowerManager.WakeLock relayWakeLock;
    private ConnectivityManager connectivity;
    private ConnectivityManager.NetworkCallback networkCallback;
    private boolean foregroundStarted;
    private BroadcastReceiver deviceWakeReceiver;

    static boolean isRunning() { return running; }

    /** A stalled worker cannot retain the CPU beyond this bounded relay lease. */
    static void renewWakeLock() {
        WebBridgeBackgroundService service = active;
        if (service == null || !running || !WebBridge.get(service).isEnabled()) return;
        synchronized (service.wakeLockGuard) {
            if (!service.foregroundStarted || !running || service.relayWakeLock == null) return;
            try { service.relayWakeLock.acquire(180000L); }
            catch (RuntimeException unavailable) {
                // Network/queue recovery remains functional without a wake-lock grant.
            }
        }
    }

    static void ensureStarted(Context source) {
        Context context = source.getApplicationContext();
        // Never acquire the bridge instance lock while holding this class's lock.
        if (!WebBridge.get(context).isEnabled()) { stop(context); return; }
        synchronized (WebBridgeBackgroundService.class) {
            long now = SystemClock.elapsedRealtime();
            if (running || startRequestedAt > 0 && now - startRequestedAt < 5000L) return;
            startRequestedAt = now;
        }
        try {
            context.startForegroundService(new Intent(context, WebBridgeBackgroundService.class));
        } catch (RuntimeException unavailable) {
            synchronized (WebBridgeBackgroundService.class) { startRequestedAt = 0; }
            WebBridge.get(context).backgroundStatus("Android chưa cho phép duy trì chạy nền. Mở GaoApp và bật gửi lại; kiểm tra quyền chạy nền của ứng dụng.");
        }
    }

    static void stop(Context source) {
        synchronized (WebBridgeBackgroundService.class) { startRequestedAt = 0; }
        WebBridgeBackgroundService service = active;
        if (service != null) service.releaseWakeLock();
        source.getApplicationContext().stopService(new Intent(source.getApplicationContext(), WebBridgeBackgroundService.class));
    }

    @Override public void onCreate() {
        super.onCreate();
        synchronized (WebBridgeBackgroundService.class) { startRequestedAt = 0; }
        try {
            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager == null) throw new IllegalStateException("Notifications unavailable");
            NotificationChannel channel = new NotificationChannel(CHANNEL, "Kết nối cửa hàng", NotificationManager.IMPORTANCE_LOW);
            channel.setDescription("Trạng thái chế độ gửi thông báo đến cửa hàng khi điện thoại khóa màn hình.");
            channel.setShowBadge(false);
            manager.createNotificationChannel(channel);
            Notification notification = notification();
            if (Build.VERSION.SDK_INT >= 34) startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
            else startForeground(NOTIFICATION_ID, notification);
            foregroundStarted = true;
            running = true;
            active = this;
            WebBridge.get(this).foregroundAvailable();
            PowerManager power = getSystemService(PowerManager.class);
            if (power != null) {
                synchronized (wakeLockGuard) {
                    relayWakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "GaoApp:WebNotificationRelay");
                    relayWakeLock.setReferenceCounted(false);
                }
                renewWakeLock();
            }
            watchNetwork();
            watchDeviceWake();
        } catch (RuntimeException unavailable) {
            WebBridge.get(this).backgroundStatus("Chưa bật được dịch vụ gửi nền. Mở GaoApp và kiểm tra quyền chạy nền của ứng dụng.");
            stopSelf();
        }
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && STOP.equals(intent.getAction())) {
            WebBridge.get(this).enabled(false);
            stopSelf();
            return START_NOT_STICKY;
        }
        if (!foregroundStarted || !WebBridge.get(this).isEnabled()) {
            stopSelf();
            return START_NOT_STICKY;
        }
        WebBridge.startWorker(this);
        return START_STICKY;
    }

    private Notification notification() {
        int immutable = PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE;
        PendingIntent open = PendingIntent.getActivity(this, NOTIFICATION_ID,
            new Intent(this, WebBridgeActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP), immutable);
        PendingIntent stop = PendingIntent.getService(this, NOTIFICATION_ID + 1,
            new Intent(this, WebBridgeBackgroundService.class).setAction(STOP), immutable);
        Notification.Builder builder = new Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_probe)
            .setContentTitle("GaoApp đang kết nối cửa hàng")
            .setContentText("Đang duy trì nhận và gửi thông báo. Chạm để xem trạng thái.")
            .setContentIntent(open)
            .setCategory(Notification.CATEGORY_SERVICE)
            .setVisibility(Notification.VISIBILITY_PRIVATE)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .setOngoing(true)
            .addAction(new Notification.Action.Builder(android.R.drawable.ic_menu_close_clear_cancel, "Ngừng gửi", stop).build());
        if (Build.VERSION.SDK_INT >= 31) builder.setForegroundServiceBehavior(Notification.FOREGROUND_SERVICE_IMMEDIATE);
        return builder.build();
    }

    private void watchNetwork() {
        connectivity = getSystemService(ConnectivityManager.class);
        if (connectivity == null) return;
        ConnectivityManager.NetworkCallback callback = new ConnectivityManager.NetworkCallback() {
            @Override public void onCapabilitiesChanged(Network network, NetworkCapabilities capabilities) {
                if (capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                        && capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)) {
                    // The worker coalesces kicks; this does not create another service or loop.
                    WebBridge.recoveryWake(getApplicationContext(), "network");
                }
            }
        };
        try {
            connectivity.registerDefaultNetworkCallback(callback);
            networkCallback = callback;
        } catch (RuntimeException unavailable) {
            // The scheduled worker and persisted recovery job remain available.
            WebBridge.get(this).backgroundStatus("Dịch vụ gửi nền đang chạy; chưa theo dõi được thay đổi mạng. App vẫn tự thử gửi lại.");
        }
    }

    private void watchDeviceWake() {
        deviceWakeReceiver = new BroadcastReceiver() {
            @Override public void onReceive(Context context, Intent intent) {
                if (intent == null || !WebBridge.get(context).isEnabled()) return;
                WebBridge.recoveryWake(context, PowerManager.ACTION_DEVICE_IDLE_MODE_CHANGED.equals(intent.getAction())
                    ? "idle-state" : "screen-unlock");
            }
        };
        IntentFilter actions = new IntentFilter();
        actions.addAction(Intent.ACTION_SCREEN_ON);
        actions.addAction(Intent.ACTION_USER_PRESENT);
        actions.addAction(PowerManager.ACTION_DEVICE_IDLE_MODE_CHANGED);
        try {
            if (Build.VERSION.SDK_INT >= 33) registerReceiver(deviceWakeReceiver, actions, Context.RECEIVER_NOT_EXPORTED);
            else registerReceiver(deviceWakeReceiver, actions);
        } catch (RuntimeException unavailable) { deviceWakeReceiver = null; }
    }

    @Override public void onTaskRemoved(Intent rootIntent) {
        try { WebBridgeRecoveryReceiver.schedule(this, WebBridge.get(this).isEnabled()); }
        catch (RuntimeException ignored) { }
        super.onTaskRemoved(rootIntent);
    }

    @Override public void onDestroy() {
        running = false;
        if (active == this) active = null;
        synchronized (WebBridgeBackgroundService.class) { startRequestedAt = 0; }
        releaseWakeLock();
        if (connectivity != null && networkCallback != null) {
            try { connectivity.unregisterNetworkCallback(networkCallback); }
            catch (RuntimeException ignored) { }
            networkCallback = null;
        }
        if (deviceWakeReceiver != null) {
            try { unregisterReceiver(deviceWakeReceiver); } catch (RuntimeException ignored) { }
            deviceWakeReceiver = null;
        }
        try { WebBridgeRecoveryReceiver.schedule(this, WebBridge.get(this).isEnabled()); }
        catch (RuntimeException ignored) { }
        if (foregroundStarted) stopForeground(STOP_FOREGROUND_REMOVE);
        foregroundStarted = false;
        super.onDestroy();
    }

    private void releaseWakeLock() {
        synchronized (wakeLockGuard) {
            if (relayWakeLock != null && relayWakeLock.isHeld()) relayWakeLock.release();
        }
    }

    @Override public IBinder onBind(Intent intent) { return null; }
}
