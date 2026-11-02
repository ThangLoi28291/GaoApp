package vn.gaoapp.acbprobe;
public final class ResourceRetryTests {
    public static void main(String[] args) {
        String[] input = {null, "", "wrong", "-5", "0", "1", "120", "9999999999", "9999999999999999999999999"};
        long[] expected = {0, 0, 0, 0, 0, 1000, 120000, 3600000, 0};
        for (int i=0;i<input.length;i++) if (WebBridgeProtocol.retryAfterMillis(input[i]) != expected[i]) throw new AssertionError("Retry-After check " + i);
        System.out.println("PASS: 9 bounded Retry-After checks");
    }
}
