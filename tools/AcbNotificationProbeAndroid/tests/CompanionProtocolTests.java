package vn.gaoapp.acbprobe;
public final class CompanionProtocolTests {
    private static int checks;
    private static void check(boolean value) { checks++; if (!value) throw new AssertionError("Companion check " + checks); }
    public static void main(String[] args) {
        String[] code = CompanionProtocol.pairingCode(" GAO1|12:34:56:78:9a:bc|12345678 ");
        check(code != null && code[1].equals("12:34:56:78:9A:BC") && code[2].equals("12345678"));
        for (String invalid : new String[]{null, "", "GAO1|12:34:56:78:9A:BC|123", "GAO1|12:34:56:78:9A:BC|abcdefgh", "GAO1|00:00:00:00:00:00|12345678", "GAO1|FF:FF:FF:FF:FF:FF|12345678", "GAO1|12:34:56:78:9A:BC|12345678|extra", "GAO1|12:34:56:78:9A:BG|12345678"}) check(CompanionProtocol.pairingCode(invalid) == null);
        String nonce = "0123456789abcdef0123456789abcdef";
        check(CompanionProtocol.validHelloReply("OK GAOAPP1 " + nonce, nonce));
        check(!CompanionProtocol.validHelloReply("OK GAOAPP1 " + nonce, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        check(!CompanionProtocol.validHelloReply("OK GAOAPP1 " + nonce + " EXTRA", nonce));
        check(!CompanionProtocol.validHelloReply("OK GAOAPP1 null", null));
        check(!CompanionProtocol.validHelloReply("+8000 VND POS-123", nonce));
        System.out.println("PASS: " + checks + " companion protocol checks");
    }
}
