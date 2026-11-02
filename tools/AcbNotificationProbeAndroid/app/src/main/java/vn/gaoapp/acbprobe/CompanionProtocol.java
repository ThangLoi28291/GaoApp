package vn.gaoapp.acbprobe;

import java.util.Locale;

/** Only health-check messages, never notification contents or credentials. */
final class CompanionProtocol {
    static final String SERVICE_UUID = "9102c46c-e477-4e82-9dba-21fba321ec70";
    static String[] pairingCode(String text) {
        if (text == null) return null;
        String[] fields = text.trim().toUpperCase(Locale.ROOT).split("\\|", -1);
        if (fields.length != 3 || !"GAO1".equals(fields[0])
            || !fields[1].matches("[0-9A-F]{2}(?::[0-9A-F]{2}){5}") || !fields[2].matches("[0-9]{8}")) return null;
        if ("00:00:00:00:00:00".equals(fields[1]) || "FF:FF:FF:FF:FF:FF".equals(fields[1])) return null;
        return fields;
    }
    static boolean validHelloReply(String reply, String nonce) {
        return nonce != null && nonce.matches("[a-f0-9]{32}") && ("OK GAOAPP1 " + nonce).equals(reply);
    }
}
