package vn.gaoapp.acbprobe;

/** Host-side tests with anonymized fixtures; no Android device or test dependencies needed. */
public final class TransferProbeParserTests {
    private static int assertions;
    public static void main(String[] args) {
        TransferProbeParser.Analysis merchant = TransferProbeParser.inspect("Cửa hàng nhận tiền thành công\n+75.000 VND từ KHACH HANG THU - POS-1838116 FT000000123456 GD TEST 141026-11:43:02. MGD: 30989");
        equal("CREDIT", merchant.kind); equal(75000L, merchant.amountVnd); equal("POS-1838116", merchant.orderCode);
        equal("FT000000123456", merchant.ftReference); equal("30989", merchant.mgdReference); equal(null, merchant.accountNumber);
        truth(merchant.bankTimeText.contains("141026-11:43:02")); truth(merchant.warnings.toString().contains("tài khoản nhận"));
        TransferProbeParser.Analysis balance = TransferProbeParser.inspect("Thông báo thay đổi số dư tại TK\nACB: TK 12345678(VND) + 180,000 VND lúc 11:12 14/10/2026. Số dư 1,150,663. GD: KHACH HANG THU CHUYEN KHOAN");
        equal("CREDIT", balance.kind); equal(180000L, balance.amountVnd); equal("12345678", balance.accountNumber);
        equal(null, balance.orderCode); equal(null, balance.ftReference); equal(null, balance.mgdReference);
        truth(balance.bankTimeText.contains("14/10/2026")); truth(balance.warnings.toString().contains("mã đơn"));
        for (String sample : new String[]{"+145.000 VND - POS-1838110. MGD: 30988", "+62.000 VND POS-1838104 MGD: 30987", "+243.000 VND POS-1838097 MGD: 30986"}) {
            equal("CREDIT", TransferProbeParser.inspect(sample).kind);
        }
        equal(75000L, TransferProbeParser.parseVnd("75,000")); equal(75000L, TransferProbeParser.parseVnd("75000"));
        equal(75000L, TransferProbeParser.parseVnd("75.000,00")); equal(75000L, TransferProbeParser.parseVnd("75,000.00"));
        equal(75000L, TransferProbeParser.parseVnd("75000.00")); equal(null, TransferProbeParser.parseVnd("75,00,0"));
        equal(null, TransferProbeParser.parseVnd("75.000,50")); equal(null, TransferProbeParser.parseVnd("75000.5"));
        equal(null, TransferProbeParser.parseVnd("99999999999999999999")); equal(null, TransferProbeParser.parseVnd("10000000000000000"));
        TransferProbeParser.Analysis debit = TransferProbeParser.inspect("ACB: TK 12345678(VND) -180,000 VND lúc 11:12 CHUYEN KHOAN POS-1838116");
        equal("DEBIT", debit.kind); equal(-180000L, debit.amountVnd);
        equal("INCOMPLETE", TransferProbeParser.inspect("+75.000 VND +145.000 VND POS-1838116").kind);
        equal("INCOMPLETE", TransferProbeParser.inspect("+75.000 VND -75.000 VND POS-1838116").kind);
        equal("INCOMPLETE", TransferProbeParser.inspect("+75,00,0 VND POS-1838116").kind);
        equal("INCOMPLETE", TransferProbeParser.inspect("+0 VND POS-1838116").kind);
        // A real 167,000 VND credit had numeric memo text after a dash; anonymized here.
        TransferProbeParser.Analysis numericMemo = TransferProbeParser.inspect("Cửa hàng nhận tiền thành công\n"
            + "+ 167.000 VND từ KHACH HANG THU tại NGAN HANG THU - 151000000000 0300000000 POS1838284 GD TEST12345678 141026-19:12:42. MGD: 31024");
        equal("CREDIT", numericMemo.kind); equal(167000L, numericMemo.amountVnd); equal("POS-1838284", numericMemo.orderCode);
        for (String suffix : new String[]{" - 151000000000 0300000000", " + 84900000000", " POS-1838284-QR-1838284-a0112233445566778899aabbccddeeff", " 151026-09:35:59"}) {
            TransferProbeParser.Analysis withMemo = TransferProbeParser.inspect("+167.000 VND MGD: 31024" + suffix);
            equal("CREDIT", withMemo.kind); equal(167000L, withMemo.amountVnd);
        }
        for (String noMoney : new String[]{"ACB TK 12345678(VND) + 180,000 lúc 11:12", "VND +84900000000 POS-1838284 MGD: 31024",
                "Thông báo POS-1838284 - 151000000000 0300000000 MGD: 31024", "Thông báo 151026-09:35:59 POS-1838284", "+167000 VNDX MGD: 31024"}) {
            TransferProbeParser.Analysis missing = TransferProbeParser.inspect(noMoney);
            equal("INCOMPLETE", missing.kind); equal(null, missing.amountVnd);
        }
        for (String wholeVnd : new String[]{"167000", "167.000", "167,000", "167 000", "167000.00", "167000,00", "167.000,00", "167,000.00", "167 000.00", "167 000,00"}) {
            TransferProbeParser.Analysis whole = TransferProbeParser.inspect("+" + wholeVnd + " VND MGD: 31024");
            equal("CREDIT", whole.kind); equal(167000L, whole.amountVnd);
        }
        for (String invalidMoney : new String[]{"167000.50", "167.000,50", "167,000.01", "16,70,00", "0", "10000000000000000"}) {
            TransferProbeParser.Analysis invalid = TransferProbeParser.inspect("+" + invalidMoney + " VND MGD: 31024");
            equal("INCOMPLETE", invalid.kind); equal(null, invalid.amountVnd);
        }
        equal("REDACTED", TransferProbeParser.inspect("Thông báo thay đổi số dư: nội dung bị ẩn").kind);
        equal(null, TransferProbeParser.inspect("+75.000 VND POS-1838116 POS-1838117").orderCode);
        equal(null, TransferProbeParser.inspect("+75.000 VND MGD: 12345 MGD: 54321").mgdReference);
        equal("1234****", TransferProbeParser.inspect("ACB TK 1234**** +75.000 VND POS-1838116").accountNumber);
        equal(null, TransferProbeParser.inspect("Thông báo số dư POS-1838116 GD 141026-11:43:02").amountVnd);
        equal("CREDIT", TransferProbeParser.inspect("+75.000 VND POS-1838116\n+75.000 VND POS-1838116 MGD: 30989").kind);
        for (String sensitive : new String[]{"Ma OTP: 123456", "SafeKey 123456", "Mã xác thực 123456", "Verification code: 123456", "+75.000 VND POS-1838116 mã bảo mật 123456"}) {
            TransferProbeParser.Analysis result = TransferProbeParser.inspect(sensitive);
            equal("SENSITIVE_IGNORED", result.kind); truth(!result.shouldRetain()); equal(null, result.amountVnd);
        }
        equal("UNRECOGNIZED", TransferProbeParser.inspect("Ưu đãi thẻ ACB cuối tuần").kind);
        truth(TransferProbeParser.inspect("Bạn có một thông báo mới").shouldRetain());
        equal(null, TransferProbeParser.inspect("Bạn có một thông báo mới").amountVnd);
        equal("UNRECOGNIZED", TransferProbeParser.inspect("ACB ONE\nBạn có một thông báo mới").kind);
        truth(TransferProbeParser.isAcbPackage("mobile.acb.com.vn"));
        truth(!TransferProbeParser.isAcbPackage("fake.mobile.acb.com.vn"));
        truth(!TransferProbeParser.isAcbPackage("com.acb.acbb.prod"));
        truth(!TransferProbeParser.isAcbPackage(null));
        equal("POS-1838116", TransferProbeParser.inspect("+75000 vnd pos - 1838116 MGD: 30989").orderCode);
        TransferProbeParser.Analysis phoneSample = TransferProbeParser.inspect("Cửa hàng nhận tiền thành công\n+ 8.000 VND từ KHACH HANG THU tại ACB - NH TMCP A CHAU - IB POS-1838200. MGD: 40123");
        equal("CREDIT", phoneSample.kind); equal(8000L, phoneSample.amountVnd); equal("POS-1838200", phoneSample.orderCode);
        equal("40123", phoneSample.mgdReference);
        // Real notification format reported after disconnecting the companion PC; sender/ref anonymized.
        TransferProbeParser.Analysis compactPos = TransferProbeParser.inspect("Cửa hàng nhận tiền thành công\n+ 172.000 VND từ KHACH HANG THU tại MB Bank - NH TMCP Quan Doi - POS1838268-GD-BNRTEST123-141026-18:54:54. MGD: 31018");
        equal("CREDIT", compactPos.kind); equal(172000L, compactPos.amountVnd);
        equal("POS-1838268", compactPos.orderCode); equal("31018", compactPos.mgdReference);
        equal(null, compactPos.accountNumber); equal(null, compactPos.ftReference);
        truth(compactPos.bankTimeText.contains("141026-18:54:54"));
        for (String order : new String[]{"POS1838268", "pos 1838268", "POS-1838268", "POS - 1838268", "ＰＯＳ１８３８２６８"}) {
            equal("POS-1838268", TransferProbeParser.inspect("+172.000 VND " + order + " MGD: 31018").orderCode);
        }
        equal("POS-1838268", TransferProbeParser.inspect("+172.000 VND POS1838268 POS-1838268").orderCode);
        TransferProbeParser.Analysis conflictingPos = TransferProbeParser.inspect("+172.000 VND POS1838268 POS-1838269");
        equal(null, conflictingPos.orderCode); truth(conflictingPos.warnings.toString().contains("Có nhiều mã đơn POS"));
        equal("INCOMPLETE", conflictingPos.kind);
        for (String invalid : new String[]{"APOS1838268", "POS1838268A", "POS1838268...", "POS1838268…", "POS1838268 ...", "POS1234567890123"}) {
            equal(null, TransferProbeParser.inspect("+172.000 VND " + invalid).orderCode);
        }
        equal("POS-1", TransferProbeParser.inspect("+172000 VND POS0000000001").orderCode);
        equal("POS-2147483647", TransferProbeParser.inspect("+172000 VND POS2147483647").orderCode);
        for (String invalid : new String[]{"POS0", "POS0000000000", "POS2147483648", "POS9999999999", "POS12345678901"}) {
            equal(null, TransferProbeParser.inspect("+172000 VND " + invalid).orderCode);
        }
        for (String invalid : new String[]{"POS0", "POS0000000000", "POS2147483648", "POS9999999999"})
            equal("INCOMPLETE", TransferProbeParser.inspect("+172000 VND " + invalid).kind);
        equal("CREDIT", TransferProbeParser.inspect("+172000 VND POS12345678901").kind);
        // A merchant bank can remove every hyphen between the POS and QR fields.
        String qrToken = "a0112233445566778899aabbccddeeff";
        for (String reference : new String[]{"POS1838418QR1838418" + qrToken,
                "POS-1838418-QR-1838418-" + qrToken, "pos1838418-qr1838418" + qrToken,
                "POS-1838418QR-1838418-" + qrToken, "POS 1838418 QR 1838418 " + qrToken}) {
            TransferProbeParser.Analysis joined = TransferProbeParser.inspect("Cửa hàng nhận tiền thành công\n"
                + "+170.000 VND từ KHACH HANG THU tại TMCP Nam A - " + reference
                + " GD TEST12345678 151026-09:35:59. MGD: 41045");
            equal("CREDIT", joined.kind); equal(170000L, joined.amountVnd); equal("POS-1838418", joined.orderCode);
        }
        equal("POS-1838418", TransferProbeParser.inspect("+170000 VND POS1838418QR1838418" + qrToken + " POS-1838418").orderCode);
        equal(null, TransferProbeParser.inspect("+170000 VND POS1838418QR1838418" + qrToken + " POS1838419").orderCode);
        equal(null, TransferProbeParser.inspect("+170000 VND POS1838418QR1838418" + qrToken + " POS1838418QR1838418b0112233445566778899aabbccddeeff").orderCode);
        equal(null, TransferProbeParser.inspect("+170000 VND POS000123QR000123" + qrToken).orderCode);
        for (String conflict : new String[]{"POS1838418QR1838419" + qrToken,
                "POS000123QR000123" + qrToken,
                "POS1838418QR1838418" + qrToken + " POS1838418QR1838418b0112233445566778899aabbccddeeff"})
            equal("INCOMPLETE", TransferProbeParser.inspect("+170000 VND " + conflict).kind);
        for (String conflict : new String[]{"POS-1838418 OTHER QR-1838419-" + qrToken,
                "POS-1838418 OTHER QR-1838418-" + qrToken + " QR-1838418-b0112233445566778899aabbccddeeff",
                "QR-1838418-" + qrToken + " QR-1838419-b0112233445566778899aabbccddeeff"}) {
            TransferProbeParser.Analysis independent = TransferProbeParser.inspect("+170000 VND " + conflict + " MGD:41045");
            equal("INCOMPLETE", independent.kind); equal(null, independent.orderCode);
        }
        equal("POS-1838418", TransferProbeParser.inspect("+170000 VND POS-1838418 OTHER QR-1838418-" + qrToken + " MGD:41045").orderCode);
        equal("CREDIT", TransferProbeParser.inspect("+170000 VND QR-1838418-" + qrToken + " MGD:41045").kind);
        equal("CREDIT", TransferProbeParser.inspect("+170000 VND POS-1838418 OTHER QR-1838419-123 MGD:41045").kind);
        for (String invalid : new String[]{"APOS1838418QR1838418" + qrToken, "POS1838418QROOPS",
                "POS1838418QR1838418" + qrToken.substring(1), "POS1838418QR1838418" + qrToken + "F",
                "POS1838418QR1838418" + qrToken + "…", "POS1838418QR1838418" + qrToken + " ...",
                "POS1838418QR1838419" + qrToken, "POS-1838418-QR-1838419-" + qrToken}) {
            equal(null, TransferProbeParser.inspect("+170000 VND " + invalid).orderCode);
        }
        for (String sample : new String[]{"Cửa\u00a0hàng\u00a0nhận\u00a0tiền\u00a0thành công\n+\u00a08.000\u00a0VND POS-1838200", "＋ ８.０００ VND POS－1838200", "+8\u202f000 VND POS-1838200", "+8.000 V\u200bND POS-1838200", "+8.000 VND POS\u20111838200"}) {
            equal("CREDIT", TransferProbeParser.inspect(sample).kind);
            equal(8000L, TransferProbeParser.inspect(sample).amountVnd);
            equal("POS-1838200", TransferProbeParser.inspect(sample).orderCode);
        }
        equal(8000L, TransferProbeParser.parseVnd("8\u202f000"));
        equal(8000L, TransferProbeParser.parseVnd("8 000.00"));
        equal(null, TransferProbeParser.parseVnd("8 00"));
        equal(null, TransferProbeParser.inspect("+8.000 VND POS-1...").orderCode);
        equal(null, TransferProbeParser.inspect("+8.000 VND POS-1838200…").orderCode);
        equal(-8000L, TransferProbeParser.inspect("ACB TK 12345678 −8.000 VND").amountVnd);
        equal("SENSITIVE_IGNORED", TransferProbeParser.inspect("ＯＴＰ： 123456").kind);
        equal("SENSITIVE_IGNORED", TransferProbeParser.inspect("Mã\u00a0xác\u00a0thực 123456").kind);
        for (String redacted : new String[]{"ACB ONE\nĐã ẩn nội dung thông báo nhạy cảm", "Sensitive notification content hidden", "Đã\u00a0ẩn nội dung thông báo nhạy cảm"}) {
            TransferProbeParser.Analysis result = TransferProbeParser.inspect(redacted);
            equal("REDACTED", result.kind); truth(result.shouldRetain()); equal(null, result.amountVnd);
            equal(null, result.orderCode); equal(null, result.accountNumber);
        }
        equal(null, TransferProbeParser.inspect("Đã ẩn nội dung thông báo nhạy cảm +8000 VND POS-1838200").amountVnd);
        equal("SENSITIVE_IGNORED", TransferProbeParser.inspect("OTP 123456 Sensitive notification content hidden").kind);
        truth(!TransferProbeParser.isRedacted("Cửa hàng nhận tiền thành công +8000 VND"));
        System.out.println("PASS: " + assertions + " parser assertions (anonymized bank samples, ambiguity, debit, OTP and package filter)");
    }
    private static void equal(Object expected, Object actual) {
        assertions++; if (expected == null ? actual != null : !expected.equals(actual))
            throw new AssertionError("Expected " + expected + ", got " + actual + " (assertion " + assertions + ")");
    }
    private static void truth(boolean value) { assertions++; if (!value) throw new AssertionError("Assertion " + assertions); }
}
