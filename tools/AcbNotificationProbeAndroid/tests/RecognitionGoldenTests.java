package vn.gaoapp.acbprobe;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.Base64;

/** The same anonymized signed-VND fixtures are consumed by the web parser tests. */
public final class RecognitionGoldenTests {
    private static int checks;
    private static void equal(Object expected, Object actual, String id, String field) {
        checks++;
        if (expected == null ? actual != null : !expected.equals(actual))
            throw new AssertionError(id + " " + field + ": expected " + expected + ", got " + actual);
    }
    public static void main(String[] args) throws Exception {
        if (args.length != 1) throw new IllegalArgumentException("Pass recognition-golden.tsv path.");
        int rows = 0;
        for (String line : Files.readAllLines(Paths.get(args[0]), StandardCharsets.UTF_8)) {
            if (line.trim().isEmpty() || line.startsWith("#") || line.startsWith("id\t")) continue;
            String[] fields = line.split("\t", -1);
            if (fields.length != 6) throw new AssertionError("Expected six fixture columns.");
            rows++;
            String id = fields[0], content = new String(Base64.getDecoder().decode(fields[5]), StandardCharsets.UTF_8);
            boolean credit = "1".equals(fields[1]);
            TransferProbeParser.Analysis analysis = TransferProbeParser.inspect(content);
            equal(credit, "CREDIT".equals(analysis.kind), id, "credit");
            equal(credit, WebBridgeProtocol.eligible(content, 100000L, 100000L, false), id, "upload eligibility");
            // Debit remains a useful local diagnostic, but cannot be uploaded as a credit.
            if (credit) {
                equal("-".equals(fields[2]) ? null : Long.valueOf(fields[2]), analysis.amountVnd, id, "amount");
                equal("-".equals(fields[3]) ? null : "POS-" + fields[3], analysis.orderCode, id, "POS");
            }
        }
        if (rows == 0) throw new AssertionError("No recognition fixtures loaded.");
        System.out.println("PASS: " + rows + " shared recognition fixtures, " + checks + " Java checks");
    }
}
