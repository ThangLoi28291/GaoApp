package vn.gaoapp.acbprobe;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Button;
import android.widget.ScrollView;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;
import java.io.InputStream;

public final class WebBridgeActivity extends Activity {
    private static final int IMPORT = 811;
    private TextView state;
    private final Handler handler = new Handler();
    private final Runnable refresh = new Runnable() {
        @Override public void run() { state.setText(WebBridge.get(WebBridgeActivity.this).status()); handler.postDelayed(this, 2000); }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        ScrollView scroll = new ScrollView(this); LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        int padding = (int)(20 * getResources().getDisplayMetrics().density); root.setPadding(padding, padding * 2, padding, padding); scroll.addView(root); setContentView(scroll);
        TextView title = new TextView(this); title.setText("Kết nối web GaoApp"); title.setTextSize(25); root.addView(title);
        TextView help = new TextView(this); help.setText("Trên web cửa hàng mở /admin/bank-notifications, tạo và tải cấu hình điện thoại. Nhập file đó tại đây. Chỉ gửi thông báo ACB mới sau khi bật; không gửi lịch sử thử cũ hoặc OTP."); root.addView(help);
        state = new TextView(this); state.setTextSize(16); root.addView(state);
        add(root, "Nhập cấu hình web", () -> { Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT); i.setType("application/json"); i.addCategory(Intent.CATEGORY_OPENABLE); startActivityForResult(i, IMPORT); });
        add(root, "Bật gửi / Thử gửi lại", () -> { WebBridge.get(this).enabled(true); BackgroundReadiness.requestNotifications(this, false); WebBridge.start(this); });
        TextView backgroundHelp = new TextView(this);
        backgroundHelp.setText("Để gửi khi khóa máy, cho phép chạy nền và thông báo trạng thái. Trên OPPO, bật thêm Cho phép hoạt động nền và Tự khởi động trong cài đặt ứng dụng nếu có. Chế độ gửi liên tục có thể dùng thêm pin; không cần giữ màn hình sáng."); root.addView(backgroundHelp);
        add(root, "Cho phép chạy khi khóa màn hình", () -> BackgroundReadiness.requestBatteryAccess(this));
        add(root, "Cho phép thông báo chạy nền", () -> BackgroundReadiness.requestNotifications(this, true));
        add(root, "Cài đặt ứng dụng / Tự khởi động", () -> BackgroundReadiness.appSettings(this));
        add(root, "Ngừng gửi web", () -> WebBridge.get(this).enabled(false));
        add(root, "Xóa hàng đợi gửi web", () -> new AlertDialog.Builder(this).setMessage("Xóa các tin chưa gửi trong hàng đợi? Lịch sử quan sát vẫn giữ, các tin này sẽ không được gửi lại.")
            .setNegativeButton("Hủy", null).setPositiveButton("Xóa hàng đợi", (d,w) -> { WebBridge.get(this).enabled(false); WebBridge.get(this).clearQueue(); }).show());
        add(root, "Về màn hình đọc ACB", this::finish);
    }
    private void add(LinearLayout root, String text, Runnable action) { Button b = new Button(this); b.setText(text); b.setOnClickListener(v -> action.run()); root.addView(b); }
    @Override protected void onResume() { super.onResume(); handler.post(refresh); WebBridge.start(this); if (WebBridge.get(this).isEnabled()) BackgroundReadiness.requestNotifications(this, false); }
    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != IMPORT || result != RESULT_OK || data == null || data.getData() == null) return;
        try (InputStream input = getContentResolver().openInputStream(data.getData())) {
            if (input == null) throw new IllegalArgumentException("Không mở được file.");
            WebBridge.get(this).configure(new JSONObject(new String(WebBridge.boundedRead(input, 16000), StandardCharsets.UTF_8)));
            BackgroundReadiness.requestNotifications(this, false);
        } catch (Exception failure) { new AlertDialog.Builder(this).setMessage(failure instanceof IllegalArgumentException ? failure.getMessage() : "Không nhập được cấu hình. Chọn file tải từ web GaoApp của cửa hàng.").setPositiveButton("Đóng", null).show(); }
    }
}
