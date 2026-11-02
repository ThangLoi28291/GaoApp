package vn.gaoapp.acbprobe;

import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.function.BooleanSupplier;

/** Coalesces notification/network wake-ups and keeps a failed cycle from killing future ticks. */
final class ResilientRelayLoop {
    private final ScheduledExecutorService executor;
    private final BooleanSupplier enabled;
    private final Runnable cycle, failure;
    private final long intervalMillis;
    private boolean queued, rerun;
    private ScheduledFuture<?> periodic;
    ResilientRelayLoop(ScheduledExecutorService executor, BooleanSupplier enabled, Runnable cycle, Runnable failure, long intervalMillis) {
        this.executor=executor; this.enabled=enabled; this.cycle=cycle; this.failure=failure; this.intervalMillis=intervalMillis;
    }
    void start() {
        if (!enabled.getAsBoolean()) return;
        synchronized (this) {
            if (!enabled.getAsBoolean()) return;
            if (periodic==null || periodic.isCancelled() || periodic.isDone())
                periodic=executor.scheduleWithFixedDelay(this::runCycle, intervalMillis, intervalMillis, TimeUnit.MILLISECONDS);
            if (queued) { rerun = true; return; }
            queued = true;
            executor.execute(this::runKick);
        }
    }
    synchronized void stop() {
        if (periodic!=null) periodic.cancel(false);
        periodic=null;
        rerun=false;
    }
    private void runKick() {
        try { runCycle(); }
        finally {
            synchronized (this) {
                if (rerun && enabled.getAsBoolean()) {
                    // A notification arriving during the previous HTTP request
                    // must not wait for the next periodic tick. A burst still
                    // queues only one follow-up, never parallel uploads.
                    rerun=false;
                    executor.execute(this::runKick);
                } else { queued=false; rerun=false; }
            }
        }
    }
    private void runCycle() {
        try { if (enabled.getAsBoolean()) cycle.run(); }
        catch (RuntimeException failed) {
            try { failure.run(); } catch (RuntimeException unavailable) { /* Next tick must survive a failed diagnostic write too. */ }
        }
    }
}
