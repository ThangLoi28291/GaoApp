package vn.gaoapp.acbprobe;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.companion.AssociationInfo;
import android.companion.AssociationRequest;
import android.companion.BluetoothDeviceFilter;
import android.companion.CompanionDeviceManager;
import android.content.Intent;
import android.content.IntentSender;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

public final class CompanionActivity extends Activity {
    private static final int SELECT_DEVICE = 702, CONNECT_PERMISSION = 703;
    private final Handler handler = new Handler();
    private EditText code;
    private TextView status;
    private String[] pending;
    private final Runnable refresh = new Runnable() { public void run() { update(); handler.postDelayed(this, 1500); } };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        ScrollView scroll = new ScrollView(this); LinearLayout page = new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL); int padding = Math.round(20 * getResources().getDisplayMetrics().density); page.setPadding(padding,padding,padding,padding); scroll.addView(page); setContentView(scroll);
        scroll.setOnApplyWindowInsetsListener((view,insets) -> {
            if (Build.VERSION.SDK_INT >= 30) { android.graphics.Insets bars = insets.getInsets(android.view.WindowInsets.Type.systemBars() | android.view.WindowInsets.Type.displayCutout()); view.setPadding(bars.left,bars.top,bars.right,bars.bottom); }
            else view.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());
            return insets;
        }); scroll.requestApplyInsets();
        label(page, "Ghép máy tính đồng hành", 24);
        label(page, "1. Mở GaoApp Companion trên máy tính, bấm Bắt đầu.\n2. Bật Bluetooth và Vị trí trên điện thoại.\n3. Nhập mã ghép trên máy tính, chọn đúng thiết bị và xác nhận khi Android/Windows hỏi.", 16);
        label(page, "Liên kết đồng hành dùng để thử đọc nội dung ACB bị che. Kết nối máy tính chỉ trao đổi tín hiệu kiểm tra; nội dung ACB vẫn lưu trên điện thoại. Sau ghép, cần thử thông báo ACB mới.", 14);
        code = new EditText(this); code.setSingleLine(true); code.setHint("GAO1|địa chỉ Bluetooth|mã 8 số"); code.setTextSize(15); page.addView(code);
        if (saved != null) { code.setText(saved.getString("code", "")); pending = CompanionProtocol.pairingCode(code.getText().toString()); }
        status = label(page, "Đang kiểm tra…", 15);
        button(page, "Ghép máy tính", this::begin);
        button(page, "Kết nối lại máy tính", () -> {
            String address = CompanionLink.preferences(this).getString("address", "");
            String pin = CompanionLink.preferences(this).getString("pin", "");
            if (!CompanionLink.associated(this, address)) { show("Chưa có máy tính đã ghép. Nhập mã mới rồi bấm Ghép máy tính."); return; }
            if (Build.VERSION.SDK_INT >= 31 && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) { show("Cấp quyền Thiết bị lân cận bằng nút Ghép máy tính."); return; }
            CompanionLink.connect(this, address, pin, false);
        });
        button(page, "Ngừng kết nối máy tính", () -> {
            CompanionLink.disconnect();
            String address = CompanionLink.preferences(this).getString("address", "");
            if (!CompanionLink.preferences(this).getBoolean("verified", false) && !address.isEmpty()) {
                try { CompanionLink.remove(this, address); } catch (RuntimeException unavailable) { show("Liên kết mới chưa được kiểm tra. Bấm Hủy ghép thiết bị để xóa liên kết."); }
            }
        });
        button(page, "Hủy ghép thiết bị", () -> new AlertDialog.Builder(this).setMessage("Xóa liên kết đồng hành của GaoApp với máy tính? Lịch sử quan sát ACB vẫn giữ.").setNegativeButton("Giữ lại", null).setPositiveButton("Hủy ghép", (d,w) -> {
            try { CompanionLink.remove(this, CompanionLink.preferences(this).getString("address", "")); } catch (RuntimeException unavailable) { show("Chưa hủy được liên kết. Kiểm tra quyền hoặc hủy liên kết GaoApp trong cài đặt thiết bị đồng hành của Android."); }
        }).show());
        button(page, "Về màn hình đọc ACB", this::finish);
    }
    @Override protected void onResume() { super.onResume(); handler.post(refresh); }
    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
    @Override protected void onSaveInstanceState(Bundle saved) { saved.putString("code", code.getText().toString()); super.onSaveInstanceState(saved); }
    private void update() { status.setText(CompanionLink.message + "\nAndroid xác nhận liên kết: " + (CompanionLink.associated(this, CompanionLink.preferences(this).getString("address", "")) ? "Có" : "Chưa") + "\nKết nối trực tiếp: " + (CompanionLink.connected ? "Đang hoạt động" : "Chưa hoạt động")); }
    private void begin() {
        pending = CompanionProtocol.pairingCode(code.getText().toString());
        if (pending == null) { show("Nhập đầy đủ mã GAO1|…|… đang hiển thị trên máy tính."); return; }
        if (!getPackageManager().hasSystemFeature(PackageManager.FEATURE_COMPANION_DEVICE_SETUP)) { show("Android trên máy này không hỗ trợ ghép thiết bị đồng hành."); return; }
        if (Build.VERSION.SDK_INT >= 31 && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) { requestPermissions(new String[]{Manifest.permission.BLUETOOTH_CONNECT}, CONNECT_PERMISSION); return; }
        associate();
    }
    @SuppressWarnings("deprecation") private void associate() {
        if (pending == null) return;
        final String address = pending[1], pin = pending[2];
        if (CompanionLink.associated(this, address)) { CompanionLink.connect(this, address, pin, !CompanionLink.preferences(this).getBoolean("verified", false)); return; }
        String previous = CompanionLink.preferences(this).getString("address", "");
        if (!previous.isEmpty() && CompanionLink.associated(this, previous)) { show("Hủy ghép máy tính cũ trước khi ghép máy tính mới."); return; }
        CompanionDeviceManager manager = getSystemService(CompanionDeviceManager.class);
        if (manager == null) { show("Dịch vụ thiết bị đồng hành chưa có trên máy này."); return; }
        AssociationRequest request = new AssociationRequest.Builder().addDeviceFilter(new BluetoothDeviceFilter.Builder().setAddress(address).build()).setSingleDevice(false).build();
        CompanionLink.message = "Đang tìm đúng Bluetooth của máy tính. Chọn và xác nhận trong hộp thoại Android.";
        CompanionDeviceManager.Callback callback = new CompanionDeviceManager.Callback() {
            private void choose(IntentSender chooser) { runOnUiThread(() -> { try { startIntentSenderForResult(chooser, SELECT_DEVICE, null, 0, 0, 0); } catch (IntentSender.SendIntentException unavailable) { show("Chưa mở được hộp thoại ghép nối của Android."); } }); }
            @Override public void onDeviceFound(IntentSender chooser) { choose(chooser); }
            @Override public void onAssociationPending(IntentSender chooser) { choose(chooser); }
            @Override public void onAssociationCreated(AssociationInfo info) {
                if (info.getDeviceMacAddress() != null && address.equalsIgnoreCase(info.getDeviceMacAddress().toString())) {
                    CompanionLink.preferences(CompanionActivity.this).edit().putString("address", address).putBoolean("verified", false).apply();
                    CompanionLink.connect(CompanionActivity.this, address, pin, true);
                }
                else runOnUiThread(() -> show("Android trả về thiết bị không khớp mã máy tính; chưa kết nối."));
            }
            @Override public void onFailure(CharSequence error) { CompanionLink.message = "Android chưa ghép được thiết bị. Kiểm tra Bluetooth, Vị trí và helper máy tính đang mở."; }
        };
        try { if (Build.VERSION.SDK_INT >= 33) manager.associate(request, getMainExecutor(), callback); else manager.associate(request, callback, handler); }
        catch (RuntimeException unavailable) { show("Android từ chối ghép thiết bị (" + unavailable.getClass().getSimpleName() + "). Chưa có quyền đồng hành mới."); }
    }
    @Override public void onRequestPermissionsResult(int request, String[] permissions, int[] grants) { super.onRequestPermissionsResult(request, permissions, grants); if (request == CONNECT_PERMISSION && grants.length > 0 && grants[0] == PackageManager.PERMISSION_GRANTED) associate(); }
    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request == SELECT_DEVICE && result == RESULT_OK && Build.VERSION.SDK_INT < 33 && pending != null && CompanionLink.associated(this, pending[1])) {
            CompanionLink.preferences(this).edit().putString("address", pending[1]).putBoolean("verified", false).apply();
            CompanionLink.connect(this, pending[1], pending[2], true);
        }
        else if (request == SELECT_DEVICE && result != RESULT_OK) CompanionLink.message = "Chưa xác nhận ghép thiết bị.";
    }
    private TextView label(LinearLayout page, String text, int size) { TextView view = new TextView(this); view.setText(text); view.setTextSize(size); view.setPadding(0,12,0,24); page.addView(view); return view; }
    private void button(LinearLayout page, String text, Runnable action) { Button view = new Button(this); view.setText(text); view.setAllCaps(false); page.addView(view); view.setOnClickListener(v -> action.run()); }
    private void show(String text) { new AlertDialog.Builder(this).setMessage(text).setPositiveButton("Đóng", null).show(); }
}
