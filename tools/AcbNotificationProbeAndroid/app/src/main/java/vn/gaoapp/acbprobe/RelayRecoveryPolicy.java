package vn.gaoapp.acbprobe;

/** Monotonic scheduling only: wake-ups cannot grant upload permission or replay notifications. */
final class RelayRecoveryPolicy {
    static final long WATCHDOG_DELAY_MILLIS = 120000L;
    static final long IDLE_JOB_DELAY_MILLIS = 15 * 60 * 1000L;
    static final long MINIMUM_JOB_DELAY_MILLIS = 10000L;
    static final long LISTENER_REBIND_INTERVAL_MILLIS = 30000L;
    static final long NETWORK_DEADLINE_MILLIS = 24000L;
    private RelayRecoveryPolicy() { }
    static boolean armWatchdog(long existingDeadline) { return existingDeadline == 0; }
    static long jobDeadline(long elapsedNow, long wallNow, long nextAttempt) {
        long delay = nextAttempt == Long.MAX_VALUE ? IDLE_JOB_DELAY_MILLIS
            : Math.max(MINIMUM_JOB_DELAY_MILLIS, Math.min(IDLE_JOB_DELAY_MILLIS, nextAttempt - wallNow));
        return elapsedNow + delay;
    }
    static boolean replaceJob(long existingDeadline, long requestedDeadline) {
        // A job already due must remain due when Android defers it; resetting its
        // minimum latency every worker tick would indefinitely postpone recovery.
        return existingDeadline == 0 || requestedDeadline < existingDeadline;
    }
    static boolean mayRebind(long elapsedNow, long lastAttempt) {
        return lastAttempt == 0 || elapsedNow - lastAttempt >= LISTENER_REBIND_INTERVAL_MILLIS;
    }
}
