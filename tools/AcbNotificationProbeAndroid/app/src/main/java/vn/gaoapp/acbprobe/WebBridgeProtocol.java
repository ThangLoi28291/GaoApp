package vn.gaoapp.acbprobe;

import java.net.URI;

final class WebBridgeProtocol {
    static boolean sameConfiguration(String api, int device, String token, String currentApi, int currentDevice, String currentToken) {
        return api.equals(currentApi) && device == currentDevice && token.equals(currentToken);
    }
    static String failureMessage(int code, int device) {
        if (code == 401 || code == 403) return "Khóa kết nối không được máy chủ chấp nhận (" + code + "). Kiểm tra địa chỉ web, cấp lại cấu hình thiết bị #" + device + " rồi nhập lại. Tin chờ vẫn được giữ.";
        return code == 0 ? "Chưa kết nối được web. Giữ hàng đợi và tự thử lại." : "Máy chủ chưa nhận tin (" + code + "). Kiểm tra cấu hình hoặc báo cáo.";
    }
    static long retryDelayMillis(int attempts) { return attempts <= 0 ? 10000L : attempts == 1 ? 30000L : 60000L; }
    static long retryAfterMillis(String value) {
        if (value == null || value.length() > 10) return 0;
        try { return Math.min(3600L, Math.max(0L, Long.parseLong(value.trim()))) * 1000L; }
        catch (NumberFormatException invalid) { return 0; }
    }
    static void validate(String api, int device, String token) {
        try {
            URI uri = new URI(api);
            if (!"https".equalsIgnoreCase(uri.getScheme()) || uri.getHost() == null || uri.getUserInfo() != null ||
                uri.getQuery() != null || uri.getFragment() != null || !uri.getPath().endsWith("/api/bank-notifications"))
                throw new IllegalArgumentException("Cấu hình cần địa chỉ HTTPS của GaoApp.");
            if (device <= 0 || token == null || !token.matches("[0-9A-Fa-f]{64}")) throw new IllegalArgumentException("Mã thiết bị không hợp lệ.");
        } catch (java.net.URISyntaxException invalid) { throw new IllegalArgumentException("Địa chỉ web không hợp lệ."); }
    }
    static boolean eligible(String content, long posted, long since, boolean truncated) {
        return posted >= since - 5000 && content != null && content.length() <= 12000 && !truncated &&
            !TransferProbeParser.isSensitive(content) && !TransferProbeParser.isRedacted(content) &&
            "CREDIT".equals(TransferProbeParser.inspect(content).kind);
    }
}
