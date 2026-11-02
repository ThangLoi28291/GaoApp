package vn.gaoapp.acbprobe;

import android.app.Notification;
import android.content.Context;
import android.os.Bundle;
import android.os.Build;
import android.view.View;
import android.view.ViewGroup;
import android.widget.RemoteViews;
import android.widget.TextView;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/** Read only the notification supplied by Android, using public APIs. Never execute a click or PendingIntent. */
final class NotificationTextReader {
    private static final int FIELD_LIMIT = 4096;
    private NotificationTextReader() { }

    @SuppressWarnings("deprecation")
    static Payload read(Context context, Notification notification) {
        Payload payload = new Payload();
        Bundle extras = notification.extras == null ? Bundle.EMPTY : notification.extras;
        try {
            JSONArray extraKeys = new JSONArray();
            for (String key : extras.keySet()) if (extraKeys.length() < 80) extraKeys.put(key);
            payload.raw.put("extraKeys", extraKeys);
            Object template = extras.get(Notification.EXTRA_TEMPLATE);
            payload.raw.put("template", template instanceof String ? template : JSONObject.NULL);
            payload.raw.put("hasContentView", notification.contentView != null);
            payload.raw.put("hasBigContentView", notification.bigContentView != null);
            payload.raw.put("hasHeadsUpContentView", notification.headsUpContentView != null);
        } catch (RuntimeException | JSONException unavailable) { payload.fieldErrors.put("notificationMetadata:" + unavailable.getClass().getSimpleName()); }
        String[] keys = { Notification.EXTRA_TITLE, Notification.EXTRA_TEXT, Notification.EXTRA_BIG_TEXT,
            Notification.EXTRA_SUB_TEXT, Notification.EXTRA_SUMMARY_TEXT, Notification.EXTRA_TITLE_BIG };
        for (String key : keys) {
            try {
                Object value = extras.get(key);
                String text = payload.add(value instanceof CharSequence ? (CharSequence) value : null);
                payload.raw.put(key, text);
                if ((Notification.EXTRA_TEXT.equals(key) || Notification.EXTRA_BIG_TEXT.equals(key)) && !text.isEmpty()) payload.bodyEmpty = false;
                if (value != null && !(value instanceof CharSequence)) payload.fieldErrors.put(key + ":" + value.getClass().getSimpleName());
            } catch (RuntimeException | JSONException unavailable) { payload.fieldErrors.put(key + ":" + unavailable.getClass().getSimpleName()); }
        }
        try {
            String ticker = payload.add(notification.tickerText);
            payload.raw.put("tickerText", ticker);
            if (!ticker.isEmpty()) payload.bodyEmpty = false;
        } catch (RuntimeException | JSONException unavailable) { payload.fieldErrors.put("tickerText:" + unavailable.getClass().getSimpleName()); }
        try {
            JSONArray saved = new JSONArray();
            CharSequence[] lines = extras.getCharSequenceArray(Notification.EXTRA_TEXT_LINES);
            if (lines != null) {
                for (int i = 0; i < Math.min(lines.length, 20); i++) {
                    String text = payload.add(lines[i]); saved.put(text);
                    if (!text.isEmpty()) payload.bodyEmpty = false;
                }
                if (lines.length > 20) payload.truncated = true;
            }
            payload.raw.put(Notification.EXTRA_TEXT_LINES, saved);
        } catch (RuntimeException | JSONException unavailable) { payload.fieldErrors.put("textLines:" + unavailable.getClass().getSimpleName()); }
        try {
            JSONArray messages = new JSONArray();
            if (Build.VERSION.SDK_INT >= 30) {
                addMessages(Notification.MessagingStyle.Message.getMessagesFromBundleArray(extras.getParcelableArray(Notification.EXTRA_MESSAGES)), messages, payload);
                addMessages(Notification.MessagingStyle.Message.getMessagesFromBundleArray(extras.getParcelableArray(Notification.EXTRA_HISTORIC_MESSAGES)), messages, payload);
            } else if (Build.VERSION.SDK_INT >= 28 && extras.containsKey(Notification.EXTRA_MESSAGES)) {
                Notification.Style restored = Notification.Builder.recoverBuilder(context, notification).getStyle();
                if (restored instanceof Notification.MessagingStyle) {
                    Notification.MessagingStyle style = (Notification.MessagingStyle) restored;
                    addMessages(style.getMessages(), messages, payload);
                    addMessages(style.getHistoricMessages(), messages, payload);
                }
            }
            payload.raw.put("messageTexts", messages);
        } catch (RuntimeException | JSONException unavailable) { payload.fieldErrors.put("messagingStyle:" + unavailable.getClass().getSimpleName()); }
        payload.combine();
        TransferProbeParser.Analysis standard = TransferProbeParser.inspect(payload.combined);
        boolean needFallback = standard.amountVnd == null || standard.orderCode == null
            || standard.ftReference == null && standard.mgdReference == null;
        // Respect redacted/sensitive content. Only inflate layouts already supplied in the notification.
        if (needFallback && !payload.containsSensitive && !payload.redactionSuspected) {
            try {
                JSONObject layouts = new JSONObject();
                readLayout(context, notification.contentView, "contentView", layouts, payload);
                readLayout(context, notification.bigContentView, "bigContentView", layouts, payload);
                readLayout(context, notification.headsUpContentView, "headsUpContentView", layouts, payload);
                payload.raw.put("remoteViewTexts", layouts);
            } catch (JSONException impossible) { throw new IllegalStateException(impossible); }
        }
        payload.combine();
        try {
            payload.raw.put("fieldErrors", payload.fieldErrors);
            payload.raw.put("redactionSuspected", payload.redactionSuspected);
        } catch (JSONException impossible) { throw new IllegalStateException(impossible); }
        return payload;
    }

    private static void addMessages(List<Notification.MessagingStyle.Message> messages, JSONArray saved, Payload payload) {
        if (messages == null) return;
        for (int i = 0; i < Math.min(messages.size(), 20); i++) {
            String text = payload.add(messages.get(i).getText()); saved.put(text);
            if (!text.isEmpty()) payload.bodyEmpty = false;
        }
        if (messages.size() > 20) payload.truncated = true;
    }

    private static void readLayout(Context context, RemoteViews layout, String name, JSONObject saved, Payload payload) throws JSONException {
        if (layout == null || payload.containsSensitive || payload.redactionSuspected) return;
        JSONArray lines = new JSONArray();
        try {
            View root = layout.apply(context, null);
            collectText(root, lines, payload, 0, new int[]{0});
            saved.put(name, lines);
        } catch (RuntimeException | LinkageError unsupported) {
            payload.fieldErrors.put(name + ":" + unsupported.getClass().getSimpleName());
        }
    }

    private static void collectText(View view, JSONArray saved, Payload payload, int depth, int[] visited) {
        if (view == null || view.getVisibility() != View.VISIBLE || payload.containsSensitive || payload.redactionSuspected) return;
        if (depth > 24 || ++visited[0] > 160 || saved.length() >= 30) { payload.truncated = true; return; }
        if (view instanceof TextView) {
            String text = payload.add(((TextView) view).getText());
            if (!text.isEmpty()) { saved.put(text); payload.bodyEmpty = false; }
        }
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) collectText(group.getChildAt(i), saved, payload, depth + 1, visited);
        }
    }

    static final class Payload {
        final JSONObject raw = new JSONObject();
        final JSONArray fieldErrors = new JSONArray();
        final Set<String> texts = new LinkedHashSet<>();
        String combined = "";
        boolean truncated;
        boolean containsSensitive;
        boolean bodyEmpty = true;
        boolean androidLimitSuspected;
        boolean redactionSuspected;

        String add(CharSequence value) {
            if (value == null) return "";
            String full = value.toString();
            if (TransferProbeParser.isSensitive(full)) containsSensitive = true;
            if (TransferProbeParser.isRedacted(full)) redactionSuspected = true;
            if (full.length() == 1024 || full.length() == 5120) androidLimitSuspected = true;
            String text = full;
            if (text.length() > FIELD_LIMIT) { text = text.substring(0, FIELD_LIMIT); truncated = true; }
            if (!text.trim().isEmpty()) texts.add(text);
            return text;
        }

        void combine() {
            combined = String.join("\n", texts);
            if (combined.length() > 24000) { combined = combined.substring(0, 24000); truncated = true; }
            try { raw.put("combinedText", combined); }
            catch (JSONException impossible) { throw new IllegalStateException(impossible); }
        }
    }
}
