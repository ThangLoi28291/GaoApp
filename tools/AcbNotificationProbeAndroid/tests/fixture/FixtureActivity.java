package vn.gaoapp.acbprobe.fixture;

import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Intent;
import android.os.Bundle;
import android.widget.TextView;
import android.widget.RemoteViews;

/** Deliberately fake, local-only source to test the OS notification pipeline on an empty emulator. */
public final class FixtureActivity extends Activity {
    private static final String CHANNEL = "gaoapp-fixture";
    @Override public void onCreate(Bundle state) { super.onCreate(state); emit(getIntent()); }
    @Override protected void onNewIntent(Intent intent) { super.onNewIntent(intent); emit(intent); }
    private void emit(Intent intent) {
        TextView label = new TextView(this);
        label.setPadding(30, 60, 30, 30);
        label.setText("GAOAPP TEST\nNguồn thông báo giả lập, không phải ACB ONE.\nKhông có đăng nhập hoặc giao dịch ngân hàng.");
        setContentView(label);
        NotificationManager manager = getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel(CHANNEL, "Mẫu kiểm thử GaoApp", NotificationManager.IMPORTANCE_DEFAULT));
        String scenario = intent.getStringExtra("case");
        int id = intent.getIntExtra("id", 101);
        if (scenario == null) return;
        String title = "MẪU KIỂM THỬ — Cửa hàng nhận tiền thành công";
        String body = "+75.000 VND từ KHACH HANG THU - POS-1838116 FT000000123456 141026-11:43:02. MGD: 30989";
        if ("balance".equals(scenario)) { title = "MẪU KIỂM THỬ — Thông báo thay đổi số dư"; body = "ACB: TK 12345678(VND) + 180,000 lúc 11:12 14/10/2026. Số dư 1,150,663. GD: KHACH HANG THU CHUYEN KHOAN"; }
        if ("debit".equals(scenario)) { title = "MẪU KIỂM THỬ — Thông báo thay đổi số dư"; body = "ACB: TK 12345678(VND) -180,000 lúc 11:12 CHUYEN KHOAN"; }
        if ("hidden".equals(scenario)) { title = "ACB ONE"; body = ""; }
        if ("system-redacted".equals(scenario)) { title = "ACB ONE"; body = "Đã ẩn nội dung thông báo nhạy cảm"; }
        if ("ambiguous".equals(scenario)) body = "+75.000 VND POS-1838116 +145.000 VND POS-1838117 MGD: 30989";
        if ("locked".equals(scenario)) body = "+75.001 VND POS-3000001 MGD: 50001";
        if ("after-restart".equals(scenario)) body = "+75.002 VND POS-3000002 MGD: 50002";
        if ("offline".equals(scenario)) body = "+75.003 VND POS-3000003 MGD: 50003";
        if ("otp".equals(scenario)) { title = "MẪU KIỂM THỬ — Mã xác thực OTP"; body = "SafeKey: 123456"; }
        if ("marketing".equals(scenario)) { title = "MẪU KIỂM THỬ — Ưu đãi"; body = "Ưu đãi thẻ cuối tuần"; }
        if ("long".equals(scenario)) {
            StringBuilder longBody = new StringBuilder(body);
            for (int i = 0; i < 100; i++) longBody.append(" | Noi dung mo rong kiem thu ").append(i);
            body = longBody.toString();
        }
        if ("truncated".equals(scenario)) {
            StringBuilder longBody = new StringBuilder(body);
            for (int i = 0; i < 250; i++) longBody.append(" | Noi dung rat dai kiem thu ").append(i);
            body = longBody.toString();
        }
        if ("burst".equals(scenario)) {
            for (int i = 0; i < 5; i++) manager.notify(id + i, build(title, "+" + (75000 + i) + " VND POS-" + (2000000 + i) + " MGD: " + (40000 + i)));
            return;
        }
        Notification notification = build(title, body);
        if ("unknown".equals(scenario)) notification = build("ACB ONE", "Bạn có một thông báo mới");
        if ("custom-views".equals(scenario) || "custom-otp".equals(scenario) || "redacted-layout".equals(scenario)) {
            RemoteViews layout = new RemoteViews("mobile.acb.com.vn", mobile.acb.com.vn.R.layout.credit_notification);
            layout.setTextViewText(mobile.acb.com.vn.R.id.title, "custom-otp".equals(scenario) ? "Mã xác thực OTP" : "Cửa hàng nhận tiền thành công");
            layout.setTextViewText(mobile.acb.com.vn.R.id.body, "custom-otp".equals(scenario) ? "SafeKey: 123456" : "+ 8.000 VND từ KHACH HANG THU tại ACB - IB POS-1838200. MGD: 40123");
            notification = new Notification.Builder(this, CHANNEL).setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle("ACB ONE").setContentText("Bạn có một thông báo mới")
                .setCustomContentView(layout).setCustomBigContentView(layout).build();
            if ("redacted-layout".equals(scenario)) notification.extras.putString(Notification.EXTRA_TEXT, "Nội dung bị ẩn");
        }
        if ("messaging".equals(scenario)) {
            notification = new Notification.Builder(this, CHANNEL).setSmallIcon(android.R.drawable.ic_dialog_info)
                .setStyle(new Notification.MessagingStyle("ACB ONE").addMessage("+8.001 VND POS-1838201 MGD: 40124", System.currentTimeMillis(), "ACB ONE")).build();
            notification.extras.putString(Notification.EXTRA_TITLE, "ACB ONE");
            notification.extras.putString(Notification.EXTRA_TEXT, "Bạn có một thông báo mới");
        }
        if ("ticker".equals(scenario)) notification = new Notification.Builder(this, CHANNEL).setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle("ACB ONE").setContentText("Bạn có một thông báo mới").setTicker("+8.002 VND POS-1838202 MGD: 40125").build();
        if ("group".equals(scenario)) notification = new Notification.Builder(this, CHANNEL).setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle(title).setContentText(body).setGroup("gaoapp-group").setGroupSummary(true).build();
        manager.notify(id, notification);
    }
    private Notification build(String title, String body) {
        return new Notification.Builder(this, CHANNEL).setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle(title).setContentText(body).setStyle(new Notification.BigTextStyle().bigText(body)).build();
    }
}
