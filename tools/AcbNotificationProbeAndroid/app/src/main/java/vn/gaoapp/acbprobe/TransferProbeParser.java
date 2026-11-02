package vn.gaoapp.acbprobe;

import java.text.Normalizer;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Diagnostic extraction only. Nothing in this class authorizes a payment. */
public final class TransferProbeParser {
    public static final String ACB_PACKAGE = "mobile.acb.com.vn";
    // Match the same signed VND field as the web receiver. A separator before
    // phone/account/reference numbers in the memo is not a second debit.
    private static final Pattern MONEY = Pattern.compile("([+-])\\s*([0-9][0-9., ]*)\\s*VND\\b");
    // Merchant notifications can contain either POS-1838268 or POS1838268-GD-... .
    private static final Pattern ORDER = Pattern.compile("(?<![A-Z0-9])POS\\s*-?\\s*([0-9]{1,10})(?![A-Z0-9]|\\s*(?:\\.\\.\\.|…))");
    // Some banks strip separators from the complete POS-{id}-QR-{id}-{Guid:N} note.
    private static final Pattern ORDER_QR = Pattern.compile("(?<![A-Z0-9])POS\\s*-?\\s*([0-9]{1,10})\\s*-?\\s*QR\\s*-?\\s*([0-9]{1,10})\\s*-?\\s*([0-9A-F]{32})(?![A-Z0-9]|\\s*(?:\\.\\.\\.|…))");
    private static final Pattern QR = Pattern.compile("\\bQR-([0-9]{1,10})-([0-9A-F]{32})(?![A-Z0-9]|\\s*(?:\\.\\.\\.|…))");
    private static final Pattern FT = Pattern.compile("\\bFT[0-9A-Z]{6,40}\\b");
    private static final Pattern MGD = Pattern.compile("\\b(?:MGD|MA\\s*(?:GD|GIAO DICH))\\s*[:.]?\\s*([0-9A-Z]{3,40})\\b");
    private static final Pattern ACCOUNT = Pattern.compile("\\b(?:TK|TAI KHOAN|STK|SO TAI KHOAN)\\s*[:.]?\\s*([0-9X*]{4,34})(?![0-9X*])");
    private static final Pattern BANK_TIME = Pattern.compile("\\b[0-9]{6}-[0-9]{2}:[0-9]{2}(?::[0-9]{2})?\\b|\\b[0-9]{2}/[0-9]{2}/[0-9]{4}\\b|\\b[0-9]{2}:[0-9]{2}(?::[0-9]{2})?\\b");
    private static final Pattern SENSITIVE = Pattern.compile("\\b(?:OTP|SAFEKEY|PASSCODE)\\b|MA XAC THUC|MA BAO MAT|MA DANG NHAP|VERIFICATION CODE|ONE.?TIME PASSWORD");

    private TransferProbeParser() { }

    public static boolean isAcbPackage(String packageName) {
        return ACB_PACKAGE.equals(packageName);
    }

    public static boolean isSensitive(String content) {
        return content != null && SENSITIVE.matcher(fold(content)).find();
    }

    public static boolean isRedacted(String content) {
        if (content == null) return false;
        String text = fold(content);
        return text.contains("DA AN NOI DUNG THONG BAO NHAY CAM")
            || text.contains("NOI DUNG BI AN") || text.contains("NOI DUNG THONG BAO DA BI AN")
            || text.contains("SENSITIVE NOTIFICATION CONTENT HIDDEN")
            || text.contains("SENSITIVE CONTENT HIDDEN") || text.contains("CONTENT HIDDEN");
    }

    public static Analysis inspect(String content) {
        String text = fold(content == null ? "" : content);
        Analysis result = new Analysis();
        // Exclude before persistence: a bank app also sends authentication notifications.
        if (SENSITIVE.matcher(text).find()) {
            result.kind = "SENSITIVE_IGNORED";
            return result;
        }
        if (isRedacted(text)) {
            result.kind = "REDACTED";
            result.warnings.add("Nội dung thông báo đã bị che trước khi app nhận được. Chưa có số tiền, mã đơn hoặc mã giao dịch để đối chiếu.");
            return result;
        }
        Set<Long> credits = new LinkedHashSet<>();
        Set<Long> debits = new LinkedHashSet<>();
        boolean malformed = false;
        Matcher money = MONEY.matcher(text);
        while (money.find()) {
            // POS-1838116 and dates are not negative amounts.
            if (money.start() > 0 && Character.isLetterOrDigit(text.charAt(money.start() - 1))) continue;
            Long parsed = parseVnd(money.group(2));
            if (parsed == null || parsed <= 0) { malformed = true; continue; }
            ("+".equals(money.group(1)) ? credits : debits).add(parsed);
        }
        boolean bankMarker = text.contains("NHAN TIEN") || text.contains("SO DU") || text.contains("CHUYEN KHOAN")
            || text.contains("GIAO DICH") || text.contains("VND") || text.contains("POS-");
        if (!bankMarker) {
            result.kind = "UNRECOGNIZED";
            result.warnings.add("Chưa nhận diện được nội dung thông báo ACB. Giữ bản gốc để kiểm tra; chưa xác định đây là giao dịch tiền vào.");
            return result;
        }
        result.kind = credits.size() == 1 && debits.isEmpty() && !malformed ? "CREDIT"
            : debits.size() == 1 && credits.isEmpty() && !malformed ? "DEBIT" : "INCOMPLETE";
        if ("CREDIT".equals(result.kind)) result.amountVnd = credits.iterator().next();
        if ("DEBIT".equals(result.kind)) result.amountVnd = -debits.iterator().next();
        if (credits.size() > 1 || debits.size() > 1 || !credits.isEmpty() && !debits.isEmpty())
            result.warnings.add("Có nhiều số tiền hoặc nhiều chiều giao dịch; cần xem nội dung gốc.");
        if (malformed) result.warnings.add("Số tiền có định dạng chưa hỗ trợ hoặc không hợp lệ.");
        result.orderCode = readOrderCode(text, result);
        if (result.orderCode != null) result.orderCode = "POS-" + result.orderCode;
        result.ftReference = oneMatch(FT, text, false, result, "mã FT");
        result.mgdReference = oneMatch(MGD, text, true, result, "mã MGD");
        result.accountNumber = oneMatch(ACCOUNT, text, true, result, "tài khoản nhận");
        Matcher bankTime = BANK_TIME.matcher(text);
        Set<String> times = new LinkedHashSet<>();
        while (bankTime.find()) times.add(bankTime.group());
        result.bankTimeText = String.join(" | ", times);
        if (result.amountVnd == null) result.warnings.add("Chưa đọc được số tiền có dấu + hoặc - rõ ràng.");
        if (result.orderCode == null) result.warnings.add("Chưa đọc được mã đơn POS.");
        else result.warnings.add("Mã POS chỉ xác định đơn; chưa xác định lần tạo QR.");
        if (result.ftReference == null && result.mgdReference == null) result.warnings.add("Chưa đọc được mã giao dịch ngân hàng.");
        if (result.accountNumber == null) result.warnings.add("Thông báo không cho biết tài khoản nhận.");
        else if (result.accountNumber.contains("*") || result.accountNumber.contains("X"))
            result.warnings.add("Số tài khoản trong thông báo bị che một phần.");
        if (result.bankTimeText.isEmpty()) result.warnings.add("Không có thời gian giao dịch trong nội dung; giờ nhận thông báo là trường riêng.");
        return result;
    }

    private static String readOrderCode(String text, Analysis result) {
        Matcher references = ORDER_QR.matcher(text);
        StringBuffer normalized = new StringBuffer();
        Set<String> qrReferences = new LinkedHashSet<>();
        boolean conflict = false;
        while (references.find()) {
            String order = canonicalOrder(references.group(1));
            if (!references.group(1).equals(references.group(2)) || order == null || !order.equals(references.group(2))) conflict = true;
            qrReferences.add(references.group(2) + "-" + references.group(3));
            references.appendReplacement(normalized, Matcher.quoteReplacement("POS-" + references.group(1)
                + " QR-" + references.group(2) + "-" + references.group(3)));
        }
        references.appendTail(normalized);
        // A bank can separate POS from the complete QR reference with other memo
        // text. The web receiver still checks that independent QR against POS.
        String qrOrder = null;
        Matcher qrMatcher = QR.matcher(normalized.toString());
        while (qrMatcher.find()) {
            qrReferences.add(qrMatcher.group(1) + "-" + qrMatcher.group(2));
            qrOrder = qrMatcher.group(1);
        }
        if (conflict || qrReferences.size() > 1) {
            uncertainOrder(result, "Mã đơn POS và mã QR không khớp, không hợp lệ hoặc có nhiều mã; chưa chọn một giá trị.");
            return null;
        }
        Set<String> orders = new LinkedHashSet<>();
        Matcher matcher = ORDER.matcher(normalized.toString());
        while (matcher.find()) orders.add(matcher.group(1));
        if (orders.size() > 1) {
            uncertainOrder(result, "Có nhiều mã đơn POS; chưa chọn một giá trị.");
            return null;
        }
        String rawOrder = orders.isEmpty() ? null : orders.iterator().next();
        String order = canonicalOrder(rawOrder);
        if (rawOrder != null && order == null) uncertainOrder(result, "Mã đơn POS không hợp lệ; chưa chọn một giá trị.");
        if (order != null && qrOrder != null && !order.equals(qrOrder)) {
            uncertainOrder(result, "Mã đơn POS và mã đơn trong QR khác nhau; chưa chọn một giá trị.");
            return null;
        }
        return order;
    }

    private static void uncertainOrder(Analysis result, String warning) {
        if ("CREDIT".equals(result.kind)) result.kind = "INCOMPLETE";
        result.warnings.add(warning);
    }

    private static String canonicalOrder(String raw) {
        if (raw == null) return null;
        try {
            int order = Integer.parseInt(raw);
            return order > 0 ? Integer.toString(order) : null;
        } catch (NumberFormatException ignored) { return null; }
    }

    private static String oneMatch(Pattern pattern, String text, boolean group, Analysis result, String name) {
        Set<String> values = new LinkedHashSet<>();
        Matcher matcher = pattern.matcher(text);
        while (matcher.find()) values.add(group ? matcher.group(1) : matcher.group());
        if (values.size() > 1) { result.warnings.add("Có nhiều " + name + "; chưa chọn một giá trị."); return null; }
        return values.isEmpty() ? null : values.iterator().next();
    }

    public static Long parseVnd(String raw) {
        // Accept whole VND in both grouping conventions, including an explicit .00 / ,00.
        if (raw == null || raw.isEmpty()) return null;
        String value = fold(raw).trim();
        if (value.matches("[0-9]{1,3}(?:\\.[0-9]{3})+,00")) value = value.substring(0, value.length() - 3);
        else if (value.matches("[0-9]{1,3}(?:,[0-9]{3})+\\.00")) value = value.substring(0, value.length() - 3);
        else if (value.matches("[0-9]+[.,]00")) value = value.substring(0, value.length() - 3);
        else if (value.matches("[0-9]{1,3}(?: [0-9]{3})+[.,]00")) value = value.substring(0, value.length() - 3);
        if (!value.matches("[0-9]+|[0-9]{1,3}(?:\\.[0-9]{3})+|[0-9]{1,3}(?:,[0-9]{3})+|[0-9]{1,3}(?: [0-9]{3})+")) return null;
        try {
            long amount = Long.parseLong(value.replace(".", "").replace(",", "").replace(" ", ""));
            return amount < 10_000_000_000_000_000L ? amount : null;
        } catch (NumberFormatException ignored) { return null; }
    }

    public static String fold(String value) {
        return Normalizer.normalize(value, Normalizer.Form.NFKD).replaceAll("\\p{M}", "")
            .replaceAll("\\p{Cf}", "").replaceAll("[\\p{Z}\\s]+", " ")
            .replaceAll("\\p{Pd}", "-").replace('\u2212', '-')
            .replace('đ', 'd').replace('Đ', 'D').toUpperCase(Locale.ROOT);
    }

    public static final class Analysis {
        public String kind = "INCOMPLETE";
        public Long amountVnd;
        public String orderCode;
        public String ftReference;
        public String mgdReference;
        public String accountNumber;
        public String bankTimeText = "";
        public final List<String> warnings = new ArrayList<>();
        public boolean shouldRetain() { return !kind.endsWith("IGNORED"); }
    }
}
