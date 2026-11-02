package vn.gaoapp.acbprobe;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** Restore an enabled relay after normal unlocked boot or an in-place APK update. */
public final class WebBridgeBootReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        if (intent == null) return;
        String action = intent.getAction();
        if (!Intent.ACTION_BOOT_COMPLETED.equals(action) && !Intent.ACTION_MY_PACKAGE_REPLACED.equals(action)) return;
        try {
            if (WebBridge.get(context).isEnabled()) WebBridge.start(context);
        } catch (RuntimeException unavailable) {
            WebBridge.get(context).backgroundStatus("Chưa tự khởi động được gửi nền. Mở GaoApp một lần và kiểm tra quyền tự khởi động/chạy nền của điện thoại.");
        }
    }
}
