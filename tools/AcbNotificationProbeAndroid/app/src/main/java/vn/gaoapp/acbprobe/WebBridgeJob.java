package vn.gaoapp.acbprobe;

/** Recover persisted uploads after the process is stopped; Android chooses execution time. */
public final class WebBridgeJob extends android.app.job.JobService {
    @Override public boolean onStartJob(android.app.job.JobParameters parameters) {
        WebBridge.recover(this, () -> jobFinished(parameters, false));
        return true;
    }
    @Override public boolean onStopJob(android.app.job.JobParameters parameters) {
        WebBridge bridge = WebBridge.get(this);
        bridge.recoveryJobStopped(android.os.Build.VERSION.SDK_INT >= 31 ? parameters.getStopReason() : 0);
        return bridge.isEnabled();
    }
}
