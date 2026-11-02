package vn.gaoapp.acbprobe;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.NotificationManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.provider.Settings;
import android.service.notification.NotificationListenerService;
import android.view.View;
import android.view.WindowInsets;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.text.NumberFormat;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public final class MainActivity extends Activity {
    private static final int INK = Color.rgb(23, 49, 47);
    private static final int MUTED = Color.rgb(88, 110, 107);
    private static final int TEAL = Color.rgb(8, 127, 114);
    private static final int BACKGROUND = Color.rgb(243, 246, 245);
    private static final int EXPORT_REQUEST = 701;
    private static final String[] SCENARIOS = { "Bình thường", "Khóa màn hình", "Thông báo dài", "Nhiều giao dịch liên tiếp", "Sau khởi động lại", "Không có mạng" };
    private final Handler handler = new Handler();
    private TextView status;
    private TextView stats;
    private LinearLayout events;
    private boolean includeRaw;
    private long displayedRevision = -1;
    private final Runnable refresh = new Runnable() {
        @Override public void run() { refreshState(); handler.postDelayed(this, 2000); }
    };

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        includeRaw = savedInstanceState != null && savedInstanceState.getBoolean("includeRaw");
        buildScreen();
    }

    @Override protected void onResume() {
        super.onResume(); handler.removeCallbacks(refresh); handler.post(refresh);
        AcbNotificationListener.resumePendingRestart(this);
        WebBridge.start(this);
        if (WebBridge.get(this).isEnabled()) BackgroundReadiness.requestNotifications(this, false);
    }

    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }

    @Override protected void onSaveInstanceState(Bundle saved) {
        saved.putBoolean("includeRaw", includeRaw); super.onSaveInstanceState(saved);
    }

    static boolean accessEnabled(Context context) {
        ComponentName component = new ComponentName(context, AcbNotificationListener.class);
        if (Build.VERSION.SDK_INT >= 27) {
            NotificationManager manager = (NotificationManager) context.getSystemService(NOTIFICATION_SERVICE);
            return manager != null && manager.isNotificationListenerAccessGranted(component);
        }
        String value = Settings.Secure.getString(context.getContentResolver(), "enabled_notification_listeners");
        if (value == null) return false;
        for (String entry : value.split(":")) if (component.equals(ComponentName.unflattenFromString(entry))) return true;
        return false;
    }

    @SuppressWarnings("deprecation")
    private void buildScreen() {
        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(BACKGROUND);
        scroll.setFillViewport(true);
        LinearLayout content = column();
        content.setPadding(dp(20), dp(16), dp(20), dp(24));
        scroll.addView(content);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            } else view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(), insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            return insets;
        });
        setContentView(scroll);
        scroll.requestApplyInsets();
        TextView badge = text("GAOAPP  /  THÔNG BÁO ACB", 12, TEAL, true);
        badge.setLetterSpacing(0.12f);
        content.addView(badge);
        content.addView(text("Thử thông báo ACB", 28, INK, true));
        content.addView(text("Kiểm tra dữ liệu điện thoại thực sự đọc được.", 15, MUTED, false));
        space(content, 18);

        LinearLayout connection = card(content);
        connection.addView(text("Kết nối đọc thông báo", 17, INK, true));
        status = text("Đang kiểm tra…", 14, MUTED, false);
        connection.addView(status);
        button(connection, "Cấp quyền đọc thông báo", () -> {
            try { startActivity(new Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)); }
            catch (RuntimeException unavailable) { toast("Mở Cài đặt → Quyền truy cập thông báo → GaoApp ACB Probe."); }
        }, true);
        LinearLayout actions = row(connection);
        button(actions, "Mở ACB ONE", () -> {
            Intent launch = getPackageManager().getLaunchIntentForPackage(TransferProbeParser.ACB_PACKAGE);
            if (launch != null) startActivity(launch); else toast("Cài ACB ONE từ Google Play trước khi thử.");
        }, false);
        button(actions, "Kết nối lại", () -> {
            if (!accessEnabled(this)) { toast("Cần bật quyền đọc thông báo trước."); return; }
            AcbNotificationListener.restartAfterPairing(this);
            toast("Đang mở lại dịch vụ đọc. Chờ trạng thái Dịch vụ đang kết nối.");
        }, false);
        connection.addView(text("Gửi thông báo mới lên web khi bạn đã nhập và bật cấu hình kết nối.", 12, MUTED, false));
        button(connection, "Kết nối web", () -> startActivity(new Intent(this, WebBridgeActivity.class)), true);

        LinearLayout companion = card(content);
        companion.addView(text("Máy tính đồng hành", 17, INK, true));
        companion.addView(text("Ghép với máy tính Bluetooth thật để thử quyền đồng hành. Hai thiết bị chỉ trao đổi tín hiệu kiểm tra; nội dung ACB vẫn lưu ở điện thoại.", 14, MUTED, false));
        button(companion, "Ghép máy tính đồng hành", () -> startActivity(new Intent(this, CompanionActivity.class)), true);

        LinearLayout session = card(content);
        session.addView(text("Kịch bản đang thử", 17, INK, true));
        session.addView(text("Chọn trước khi thực hiện giao dịch để ghi nhãn kết quả.", 13, MUTED, false));
        Spinner scenario = new Spinner(this);
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, SCENARIOS);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        scenario.setAdapter(adapter);
        String selected = getSharedPreferences("probe-settings", MODE_PRIVATE).getString("scenario", SCENARIOS[0]);
        for (int i = 0; i < SCENARIOS.length; i++) if (SCENARIOS[i].equals(selected)) scenario.setSelection(i);
        scenario.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                getSharedPreferences("probe-settings", MODE_PRIVATE).edit().putString("scenario", SCENARIOS[position]).apply();
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        session.addView(scenario, new LinearLayout.LayoutParams(-1, dp(48)));
        button(session, "Đọc thông báo ACB đang hiện", () -> {
            toast(AcbNotificationListener.scanActive() ? "Đã quét. Bản ghi được gắn nhãn ACTIVE_SCAN; không phải giao dịch mới." : "Dịch vụ chưa kết nối. Kiểm tra quyền hoặc bấm Kết nối lại.");
        }, false);
        button(session, "Xem mẫu minh họa từ ảnh", this::showSample, false);

        LinearLayout evidence = card(content);
        evidence.addView(text("Dữ liệu đã quan sát", 17, INK, true));
        stats = text("Chưa có dữ liệu.", 13, MUTED, false);
        evidence.addView(stats);
        LinearLayout reportActions = row(evidence);
        button(reportActions, "Xuất báo cáo", this::chooseExport, true);
        button(reportActions, "Xóa dữ liệu thử", () -> new AlertDialog.Builder(this)
            .setTitle("Xóa dữ liệu thử trên máy?")
            .setMessage("Xóa lịch sử thông báo và bộ đếm cục bộ. Quyền đọc thông báo vẫn giữ nguyên.")
            .setNegativeButton("Giữ lại", null)
            .setPositiveButton("Xóa", (dialog, which) -> ProbeStore.IO.execute(() -> {
                ProbeStore.get(this).clear(); runOnUiThread(() -> { refreshState(); toast("Đã xóa dữ liệu thử."); });
            })).show(), false);
        evidence.addView(text("Giữ tối đa 200 bản ghi, kể cả thông báo ACB chưa nhận diện. Bỏ qua OTP/SafeKey và thông báo tổng hợp nhóm.", 12, MUTED, false));

        content.addView(text("Thông báo gần nhất", 19, INK, true));
        events = column(); content.addView(events);
        space(content, 8);
        content.addView(text("Bản thử nghiệm " + AppVersion.name(this) + " • Android 8 trở lên\nDữ liệu này chưa được xác minh bằng API ngân hàng.", 12, MUTED, false));
    }

    private void refreshState() {
        ProbeStore store = ProbeStore.get(this);
        boolean permission = accessEnabled(this);
        boolean installed = AcbNotificationListener.bankAppIdentity(this).optBoolean("installed");
        status.setText((permission ? "Quyền đã bật" : "Chưa cấp quyền") + " · "
            + (ProbeStore.listenerConnected ? "Dịch vụ đang kết nối" : "Dịch vụ chưa kết nối")
            + "\n" + (installed ? "Đã cài ACB ONE" : "Chưa tìm thấy ACB ONE")
            + " · " + Build.MANUFACTURER + " " + Build.MODEL + " · Android " + Build.VERSION.RELEASE
            + ("CONFIRMED".equals(AcbNotificationListener.lifecycleEvidence(this).optString("restartStatus")) ? "\nĐã mở lại bộ đọc thông báo." : "")
            + (ProbeStore.listenerError.isEmpty() ? "" : "\n" + ProbeStore.listenerError));
        JSONObject counters = store.metrics();
        stats.setText("Nhận từ ACB: " + counters.optLong("acbCallbacks") + " lượt · lưu " + counters.optLong("uniqueCaptured") + " bản nội dung"
            + "\nChưa nhận diện: " + counters.optLong("unrecognizedCallbacks") + " lượt (đã giữ nội dung)"
            + "\nNội dung bị che: " + counters.optLong("redactedCallbacks") + " lượt"
            + "\nBỏ qua: " + counters.optLong("sensitiveIgnored") + " xác thực, " + counters.optLong("otherIgnored") + " từ bộ lọc cũ, "
            + counters.optLong("groupSummariesIgnored") + " tổng hợp"
            + (counters.optLong("evicted") > 0 ? "\nĐã loại " + counters.optLong("evicted") + " bản cũ do giới hạn lưu." : "")
            + "\nACB gần nhất: " + date(counters.optLong("lastAcbObserved")));
        if (displayedRevision == store.revision()) return;
        displayedRevision = store.revision();
        events.removeAllViews();
        List<JSONObject> items = store.recent(30);
        if (items.isEmpty()) {
            LinearLayout empty = card(events);
            empty.addView(text("Chưa có nội dung ACB được lưu", 16, INK, true));
            empty.addView(text("Nếu ACB đang có thông báo trên thanh thông báo, bấm Đọc thông báo ACB đang hiện. Bản chưa nhận diện cũng được giữ để kiểm tra. Các bản đã bị bộ lọc cũ bỏ qua chỉ lấy lại được nếu thông báo vẫn còn hiện.", 14, MUTED, false));
        }
        for (JSONObject event : items) {
            JSONObject analysis = event.optJSONObject("analysis");
            if (analysis == null) continue;
            LinearLayout item = card(events);
            String kind = observedKind(event);
            String amount = analysis.isNull("amountVnd") ? "Chưa đọc được số tiền" : money(analysis.optLong("amountVnd"));
            if ("REDACTED".equals(kind)) amount = "Nội dung bị hệ thống che";
            item.addView(text(amount, 23, "CREDIT".equals(kind) ? TEAL : INK, true));
            item.addView(text(kindLabel(kind) + " · " + date(event.optLong("lastObservedAtEpochMs")), 12, MUTED, false));
            if ("REDACTED".equals(kind)) item.addView(text("Quyền đọc đã có, nhưng Android chỉ trả thông báo đã che. App chưa nhận được số tiền và mã giao dịch.", 14, MUTED, false));
            item.addView(text(value(analysis, "orderCode") + "  |  MGD: " + value(analysis, "mgdReference"), 14, INK, true));
            item.addView(text(event.optString("scenario") + " · " + event.optString("firstTrigger") + " · " + event.optInt("deliveryCount", 1) + " lượt nhận/quét", 12, MUTED, false));
            item.addView(text("Chạm để xem nội dung và trường còn thiếu →", 12, TEAL, false));
            item.setOnClickListener(view -> showEvent(event));
            item.setContentDescription(amount + ", " + value(analysis, "orderCode") + ", xem chi tiết");
        }
        if (items.size() == 30) events.addView(text("Hiển thị 30 bản gần nhất. Xuất báo cáo để lấy toàn bộ dữ liệu đang giữ.", 12, MUTED, false));
    }

    private void showEvent(JSONObject event) {
        JSONObject analysis = event.optJSONObject("analysis");
        if (analysis == null) return;
        StringBuilder detail = new StringBuilder();
        if ("REDACTED".equals(observedKind(event))) detail.append("Android chỉ cấp nội dung đã che cho app. Chưa có dữ liệu để đối chiếu thanh toán.\n\n");
        detail.append("Loại: ").append(kindLabel(observedKind(event)))
            .append("\nSố tiền: ").append(analysis.isNull("amountVnd") ? "Chưa đọc được" : money(analysis.optLong("amountVnd")))
            .append("\nMã đơn: ").append(value(analysis, "orderCode"))
            .append("\nFT: ").append(value(analysis, "ftReference"))
            .append("\nMGD: ").append(value(analysis, "mgdReference"))
            .append("\nTài khoản trong nội dung: ").append(value(analysis, "accountNumber"))
            .append("\nThời gian trong nội dung: ").append(value(analysis, "bankTimeText"))
            .append("\nAndroid đăng thông báo: ").append(date(event.optLong("notificationPostedAtEpochMs")))
            .append("\nLần đầu quan sát: ").append(date(event.optLong("firstObservedAtEpochMs")))
            .append("\nKịch bản: ").append(event.optString("scenario"))
            .append("\nNguồn: ").append(event.optString("firstTrigger"));
        JSONArray warnings = analysis.optJSONArray("warnings");
        detail.append("\n\nCác điểm cần kiểm tra:");
        if (warnings != null) for (int i = 0; i < warnings.length(); i++) detail.append("\n• ").append(warnings.optString(i));
        JSONObject raw = event.optJSONObject("raw");
        detail.append("\n\nNội dung Android trả về:\n").append(raw == null ? "Không có" : raw.optString("combinedText", "(trống)"));
        detail.append("\n\nCác trường gốc:\n");
        try { detail.append(raw == null ? "Không có" : raw.toString(2)); } catch (JSONException ignored) { }
        showTextDialog("Chi tiết quan sát", detail.toString());
    }

    private void showSample() {
        String sample = "Cửa hàng nhận tiền thành công\n+75.000 VND từ KHACH HANG THU tại NGAN HANG THU - POS-1838116 FT000000123456 GD TEST-141026-11:43:02. MGD: 30989";
        TransferProbeParser.Analysis parsed = TransferProbeParser.inspect(sample);
        StringBuilder detail = new StringBuilder("DỮ LIỆU MẪU — không phải thông báo nhận trên điện thoại. Không lưu vào lịch sử.\n\n")
            .append(sample).append("\n\nSố tiền: ").append(parsed.amountVnd == null ? "Chưa đọc được" : money(parsed.amountVnd))
            .append("\nMã đơn: ").append(parsed.orderCode).append("\nFT: ").append(parsed.ftReference)
            .append("\nMGD: ").append(parsed.mgdReference).append("\n\n");
        for (String warning : parsed.warnings) detail.append("• ").append(warning).append("\n");
        showTextDialog("Mẫu minh họa", detail.toString());
    }

    private void chooseExport() {
        LinearLayout options = column(); options.setPadding(dp(20), dp(8), dp(20), dp(8));
        options.addView(text("Báo cáo được lưu vào nơi bạn chọn. Mặc định xuất các trường đã đọc và che số tài khoản.", 14, INK, false));
        CheckBox raw = new CheckBox(this);
        raw.setText("Kèm nội dung gốc để chẩn đoán"); raw.setChecked(false);
        options.addView(raw);
        options.addView(text("Nội dung gốc có thể chứa tên khách hàng và số tài khoản. Chỉ chọn khi cần đối chiếu chi tiết.", 12, MUTED, false));
        new AlertDialog.Builder(this).setTitle("Xuất báo cáo thử máy").setView(options)
            .setNegativeButton("Đóng", null).setPositiveButton("Chọn nơi lưu", (dialog, which) -> {
                includeRaw = raw.isChecked();
                Intent create = new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("application/json");
                create.putExtra(Intent.EXTRA_TITLE, "gaoapp-acb-step1-" + new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.ROOT).format(new Date()) + ".json");
                try { startActivityForResult(create, EXPORT_REQUEST); }
                catch (RuntimeException unavailable) { toast("Thiết bị chưa có ứng dụng chọn nơi lưu tài liệu."); }
            }).show();
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != EXPORT_REQUEST || result != RESULT_OK || data == null || data.getData() == null) return;
        final Uri target = data.getData(); final boolean withRaw = includeRaw;
        ProbeStore.IO.execute(() -> {
            try (OutputStream output = getContentResolver().openOutputStream(target, "wt")) {
                if (output == null) throw new java.io.IOException();
                output.write(ProbeStore.get(this).report(this, withRaw).toString(2).getBytes(StandardCharsets.UTF_8));
                runOnUiThread(() -> toast("Đã lưu báo cáo thử máy."));
            } catch (Exception failure) { runOnUiThread(() -> toast("Chưa lưu được báo cáo. Chọn lại nơi lưu.")); }
        });
    }

    private void showTextDialog(String title, String value) {
        ScrollView scroll = new ScrollView(this);
        TextView body = text(value, 14, INK, false);
        body.setPadding(dp(20), dp(12), dp(20), dp(12)); body.setTextIsSelectable(true);
        scroll.addView(body);
        new AlertDialog.Builder(this).setTitle(title).setView(scroll).setPositiveButton("Đóng", null).show();
    }

    private LinearLayout column() { LinearLayout view = new LinearLayout(this); view.setOrientation(LinearLayout.VERTICAL); return view; }
    private LinearLayout row(LinearLayout parent) { LinearLayout view = new LinearLayout(this); view.setOrientation(LinearLayout.HORIZONTAL); parent.addView(view); return view; }

    private LinearLayout card(LinearLayout parent) {
        LinearLayout view = column(); view.setPadding(dp(16), dp(16), dp(16), dp(16));
        GradientDrawable background = new GradientDrawable(); background.setColor(Color.WHITE); background.setCornerRadius(dp(18));
        background.setStroke(dp(1), Color.rgb(222, 233, 229)); view.setBackground(background);
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2); params.bottomMargin = dp(14);
        parent.addView(view, params); return view;
    }

    private TextView text(String value, int size, int color, boolean bold) {
        TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(color);
        view.setPadding(0, dp(3), 0, dp(5)); view.setLineSpacing(dp(2), 1);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD); return view;
    }

    private void button(LinearLayout parent, String label, Runnable action, boolean primary) {
        Button button = new Button(this); button.setText(label); button.setAllCaps(false); button.setTextSize(14);
        button.setTextColor(primary ? Color.WHITE : TEAL); button.setMinHeight(dp(48));
        GradientDrawable background = new GradientDrawable(); background.setCornerRadius(dp(12));
        background.setColor(primary ? TEAL : Color.rgb(233, 245, 241)); button.setBackground(background);
        button.setPadding(dp(10), dp(8), dp(10), dp(8));
        LinearLayout.LayoutParams params = parent.getOrientation() == LinearLayout.HORIZONTAL
            ? new LinearLayout.LayoutParams(0, -2, 1) : new LinearLayout.LayoutParams(-1, -2);
        params.topMargin = dp(10); params.bottomMargin = dp(4);
        if (parent.getOrientation() == LinearLayout.HORIZONTAL && parent.getChildCount() > 0) params.leftMargin = dp(8);
        parent.addView(button, params); button.setOnClickListener(view -> action.run());
    }

    private void space(LinearLayout parent, int size) { View view = new View(this); parent.addView(view, new LinearLayout.LayoutParams(1, dp(size))); }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private void toast(String value) { Toast.makeText(this, value, Toast.LENGTH_LONG).show(); }
    private static String money(long value) { return (value > 0 ? "+" : "") + NumberFormat.getIntegerInstance(new Locale("vi", "VN")).format(value) + " đ"; }
    private static String date(long value) { return value <= 0 ? "Chưa có" : new SimpleDateFormat("dd/MM HH:mm:ss", new Locale("vi", "VN")).format(new Date(value)); }
    private static String value(JSONObject object, String key) { return object.isNull(key) || object.optString(key).isEmpty() ? "Chưa có" : object.optString(key); }
    private static String observedKind(JSONObject event) {
        JSONObject raw = event.optJSONObject("raw");
        if (raw != null && (raw.optBoolean("redactionSuspected") || TransferProbeParser.isRedacted(raw.optString("combinedText")))) return "REDACTED";
        JSONObject analysis = event.optJSONObject("analysis");
        return analysis == null ? "INCOMPLETE" : analysis.optString("kind");
    }
    private static String kindLabel(String kind) { return "CREDIT".equals(kind) ? "Tiền vào (quan sát)" : "DEBIT".equals(kind) ? "Tiền ra (đối chứng)" : "REDACTED".equals(kind) ? "Thông báo bị che nội dung" : "UNRECOGNIZED".equals(kind) ? "Thông báo ACB chưa nhận diện" : "Thiếu dữ liệu / cần kiểm tra"; }
}
