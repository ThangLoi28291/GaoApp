package vn.gaoapp.acbprobe;

public final class WebBridgeProtocolTests {
    private static int checks;
    public static void main(String[] args) {
        String token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        check(WebBridgeProtocol.sameConfiguration("https://store/api/bank-notifications", 17, token, "https://store/api/bank-notifications", 17, token));
        check(!WebBridgeProtocol.sameConfiguration("https://store/api/bank-notifications", 17, token, "https://store/api/bank-notifications", 17, token.replace('0', '1')));
        check(!WebBridgeProtocol.sameConfiguration("https://store/api/bank-notifications", 17, token, "https://other/api/bank-notifications", 17, token));
        check(!WebBridgeProtocol.sameConfiguration("https://store/api/bank-notifications", 17, token, "https://store/api/bank-notifications", 18, token));
        check(WebBridgeProtocol.retryDelayMillis(0) == 10000L);
        check(WebBridgeProtocol.retryDelayMillis(1) == 30000L);
        check(WebBridgeProtocol.retryDelayMillis(2) == 60000L);
        check(WebBridgeProtocol.retryDelayMillis(100) == 60000L);
        check(WebBridgeProtocol.failureMessage(401, 17).contains("thiết bị #17"));
        check(WebBridgeProtocol.failureMessage(401, 17).contains("Tin chờ vẫn được giữ"));
        check(WebBridgeProtocol.failureMessage(403, 17).contains("403"));
        check(WebBridgeProtocol.failureMessage(0, 17).contains("tự thử lại"));
        WebBridgeProtocol.validate("https://store.example.com/api/bank-notifications", 1, token); checks++;
        WebBridgeProtocol.validate("https://store.example.com/gao/api/bank-notifications", 1, token); checks++;
        for (String api : new String[]{"http://store.example.com/api/bank-notifications", "file:///api/bank-notifications", "https://user:secret@store.example.com/api/bank-notifications", "https://store.example.com/api/bank-notifications?token=secret", "https://store.example.com/api/bank-notifications#fragment", "https://store.example.com/api/other", "https:///api/bank-notifications"}) {
            try { WebBridgeProtocol.validate(api, 1, token); throw new AssertionError("Unsafe URL accepted"); } catch (IllegalArgumentException expected) { checks++; }
        }
        for (String bad : new String[]{"", "OTP", token + "0", token.substring(1), token.replace('0','Z')}) {
            try { WebBridgeProtocol.validate("https://store.example.com/api/bank-notifications", 1, bad); throw new AssertionError("Invalid token accepted"); } catch (IllegalArgumentException expected) { checks++; }
        }
        try { WebBridgeProtocol.validate("https://store.example.com/api/bank-notifications", 0, token); throw new AssertionError("Invalid device accepted"); } catch (IllegalArgumentException expected) { checks++; }
        String credit = "Cửa hàng nhận tiền thành công +196.000 VND từ KHACH HANG THU tại ACB GD TEST12345678 MGD: 31021";
        check(WebBridgeProtocol.eligible(credit, 100000, 100000, false));
        check(!WebBridgeProtocol.eligible(credit, 94000, 100000, false));
        check(!WebBridgeProtocol.eligible(credit, 100000, 100000, true));
        check(!WebBridgeProtocol.eligible("OTP 123456 +196.000 VND", 100000, 100000, false));
        check(!WebBridgeProtocol.eligible("Đã ẩn nội dung thông báo nhạy cảm", 100000, 100000, false));
        check(!WebBridgeProtocol.eligible("-196.000 VND MGD: 31021", 100000, 100000, false));
        check(!WebBridgeProtocol.eligible(null, 100000, 100000, false));
        String numericMemoCredit = "Cửa hàng nhận tiền thành công +167.000 VND từ KHACH HANG THU tại NGAN HANG THU"
            + " - 151000000000 0300000000 POS1838284 GD TEST12345678 141026-19:12:42. MGD: 31024";
        check(WebBridgeProtocol.eligible(numericMemoCredit,100000,100000,false));
        check(!WebBridgeProtocol.eligible(numericMemoCredit,94000,100000,false));
        check(!WebBridgeProtocol.eligible(numericMemoCredit,100000,100000,true));
        check(!WebBridgeProtocol.eligible("VND +84900000000 POS-1838284 MGD: 31024",100000,100000,false));
        check(!WebBridgeProtocol.eligible("Thông báo POS-1838284 - 151000000000 0300000000 MGD: 31024",100000,100000,false));
        check(!WebBridgeProtocol.eligible("-167.000 VND MGD: 31024",100000,100000,false));
        check(!WebBridgeProtocol.eligible(numericMemoCredit+" -20.000 VND",100000,100000,false));
        check(!WebBridgeProtocol.eligible(numericMemoCredit+" OTP 123456",100000,100000,false));
        check(!WebBridgeProtocol.eligible("Đã ẩn nội dung thông báo nhạy cảm "+numericMemoCredit,100000,100000,false));
        String qrToken="a0112233445566778899aabbccddeeff";
        for(String invalid : new String[]{"POS0","POS2147483648","POS1838284 POS1838285",
                "POS1838284QR1838285"+qrToken,"POS000123QR000123"+qrToken,
                "POS1838284QR1838284"+qrToken+" POS1838284QR1838284b0112233445566778899aabbccddeeff",
                "POS-1838284 OTHER QR-1838285-"+qrToken,
                "POS-1838284 OTHER QR-1838284-"+qrToken+" QR-1838284-b0112233445566778899aabbccddeeff"})
            check(!WebBridgeProtocol.eligible("+167000 VND "+invalid+" MGD:31024",100000,100000,false));
        System.out.println("PASS: " + checks + " web upload protocol checks");
    }
    private static void check(boolean value) { checks++; if (!value) throw new AssertionError("Failed upload check " + checks); }
}
