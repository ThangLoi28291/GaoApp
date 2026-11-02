package vn.gaoapp.acbprobe;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.SystemClock;

/** Independent, inexact idle watchdog. Android/OEM policy can still defer its delivery. */
public final class WebBridgeRecoveryReceiver extends BroadcastReceiver {
    private static final int REQUEST = 42019;
    private static long scheduledAt;
    private static PendingIntent operation(Context context) {
        return PendingIntent.getBroadcast(context, REQUEST, new Intent(context, WebBridgeRecoveryReceiver.class),
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }
    static synchronized void schedule(Context context, boolean enabled) {
        AlarmManager alarms = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        if (alarms == null) return;
        if (!enabled) { alarms.cancel(operation(context)); scheduledAt = 0; return; }
        if (!RelayRecoveryPolicy.armWatchdog(scheduledAt)) return; // Keep an overdue alarm instead of pushing it into the future.
        long requested = SystemClock.elapsedRealtime() + RelayRecoveryPolicy.WATCHDOG_DELAY_MILLIS;
        alarms.setAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP, requested, operation(context));
        scheduledAt = requested;
    }
    @Override public void onReceive(Context context, Intent intent) {
        synchronized (WebBridgeRecoveryReceiver.class) { scheduledAt = 0; }
        // No historical notification scan and no enable(true): revoked/paused
        // configurations must remain stopped even if an old alarm was delivered.
        if (!WebBridge.get(context).isEnabled()) return;
        WebBridge.recoveryWake(context, "watchdog");
    }
}
