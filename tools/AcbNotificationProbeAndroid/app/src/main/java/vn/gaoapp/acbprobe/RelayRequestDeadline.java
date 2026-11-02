package vn.gaoapp.acbprobe;

import java.net.HttpURLConnection;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/** A separate clock bounds slow response bodies/writes, not just each individual read. */
final class RelayRequestDeadline implements AutoCloseable {
    private final AtomicBoolean finished = new AtomicBoolean();
    private final ScheduledFuture<?> timeout;
    private volatile boolean expired;
    RelayRequestDeadline(HttpURLConnection connection, ScheduledExecutorService watchdog, long milliseconds) {
        timeout = watchdog.schedule(() -> {
            if (!finished.compareAndSet(false, true)) return;
            expired = true;
            try { connection.disconnect(); } catch (RuntimeException ignored) { }
        }, milliseconds, TimeUnit.MILLISECONDS);
    }
    boolean expired() { return expired; }
    @Override public void close() {
        if (finished.compareAndSet(false, true)) timeout.cancel(false);
    }
}
