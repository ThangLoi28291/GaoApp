package vn.gaoapp.acbprobe;

import android.Manifest;
import android.app.Activity;
import android.app.NotificationManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.PowerManager;
import android.provider.Settings;
import android.widget.Toast;

/** User-visible setup for continuous bank notification delivery, without ADB. */
final class BackgroundReadiness {
    private BackgroundReadiness() { }
    static boolean batteryExempt(Context context) {
        PowerManager power = (PowerManager) context.getSystemService(Context.POWER_SERVICE);
        return power != null && power.isIgnoringBatteryOptimizations(context.getPackageName());
    }
    static boolean notificationsAllowed(Context context) {
        NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        return manager != null && manager.areNotificationsEnabled() &&
            (Build.VERSION.SDK_INT < 33 || context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED);
    }
    static void requestNotifications(Activity activity, boolean explicit) {
        if (notificationsAllowed(activity)) return;
        android.content.SharedPreferences setup = activity.getSharedPreferences("background-setup", Context.MODE_PRIVATE);
        boolean asked = setup.getBoolean("notificationAsked", false);
        if (!explicit && asked) return;
        setup.edit().putBoolean("notificationAsked", true).apply();
        if (Build.VERSION.SDK_INT >= 33 && activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            if (explicit && asked && !activity.shouldShowRequestPermissionRationale(Manifest.permission.POST_NOTIFICATIONS))
                openSettings(activity, new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, activity.getPackageName()));
            else activity.requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 812);
        }
        else if (explicit) openSettings(activity, new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, activity.getPackageName()));
    }
    static void requestBatteryAccess(Activity activity) {
        if (batteryExempt(activity)) { Toast.makeText(activity, "Đã cho phép chạy khi khóa màn hình.", Toast.LENGTH_SHORT).show(); return; }
        // Only opened by the user's button. Network and CPU in Doze are needed
        // for the app's core function: relaying new bank receipts to the till.
        openSettings(activity, new Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,
            Uri.parse("package:" + activity.getPackageName())));
    }
    static void appSettings(Activity activity) {
        openSettings(activity, new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
            Uri.parse("package:" + activity.getPackageName())));
    }
    private static void openSettings(Activity activity, Intent intent) {
        try { activity.startActivity(intent); }
        catch (android.content.ActivityNotFoundException missing) {
            Toast.makeText(activity, "Mở Cài đặt điện thoại → Ứng dụng → GaoApp ACB Probe → Pin và cho phép chạy nền.", Toast.LENGTH_LONG).show();
        }
    }
}
