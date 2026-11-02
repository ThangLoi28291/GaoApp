package vn.gaoapp.acbprobe;

import android.content.Context;
import android.content.pm.PackageManager;

final class AppVersion {
    private AppVersion() { }
    static String name(Context context) {
        try { return context.getPackageManager().getPackageInfo(context.getPackageName(), 0).versionName; }
        catch (PackageManager.NameNotFoundException impossible) { return "unknown"; }
    }
}
