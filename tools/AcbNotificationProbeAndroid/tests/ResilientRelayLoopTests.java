package vn.gaoapp.acbprobe;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ScheduledThreadPoolExecutor;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.net.HttpURLConnection;
import java.net.URL;

public final class ResilientRelayLoopTests {
    private static int checks;
    public static void main(String[] args) throws Exception {
        ScheduledThreadPoolExecutor executor=new ScheduledThreadPoolExecutor(1);
        executor.setRemoveOnCancelPolicy(true);
        try {
            AtomicBoolean enabled=new AtomicBoolean(true);
            AtomicInteger calls=new AtomicInteger(), failures=new AtomicInteger();
            CountDownLatch recovered=new CountDownLatch(1);
            ResilientRelayLoop loop=new ResilientRelayLoop(executor,enabled::get,()->{
                if(calls.incrementAndGet()==1)throw new IllegalStateException("temporary storage error");
                recovered.countDown();
            },()->{failures.incrementAndGet();throw new IllegalStateException("diagnostic storage error");},10);
            loop.start();check(recovered.await(3,TimeUnit.SECONDS),"A failed cycle and diagnostic must not kill future heartbeat ticks");
            enabled.set(false);loop.stop();executor.submit(()->{}).get(3,TimeUnit.SECONDS);
            check(calls.get()>=2,"worker resumed without opening an Activity");check(failures.get()==1,"one error was reported");
            check(executor.getQueue().isEmpty(),"stop removes repeating work");

            calls.set(0);CountDownLatch entered=new CountDownLatch(1),release=new CountDownLatch(1);
            loop=new ResilientRelayLoop(executor,enabled::get,()->{
                calls.incrementAndGet();entered.countDown();
                try { if(!release.await(3,TimeUnit.SECONDS))throw new AssertionError("test did not release worker"); }
                catch(InterruptedException e){Thread.currentThread().interrupt();throw new RuntimeException(e);}
            },()->failures.incrementAndGet(),3600000);
            loop.start();check(executor.getQueue().isEmpty(),"disabled relay never schedules or sends");
            enabled.set(true);loop.start();check(entered.await(3,TimeUnit.SECONDS),"enabled relay sends immediately");
            for(int i=0;i<100;i++)loop.start();
            check(executor.getQueue().size()==1,"notification/network bursts queue only the future tick, no hundred uploads");
            enabled.set(false);loop.stop();release.countDown();executor.submit(()->{}).get(3,TimeUnit.SECONDS);
            check(calls.get()==1,"coalesced wake-ups run once");check(executor.getQueue().isEmpty(),"no work remains after disable");

            CountDownLatch executorBusy=new CountDownLatch(1),unblock=new CountDownLatch(1);
            executor.execute(()->{executorBusy.countDown();try{unblock.await(3,TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}});
            check(executorBusy.await(3,TimeUnit.SECONDS),"test executor held before queued upload");
            enabled.set(true);loop.start();enabled.set(false);loop.stop();unblock.countDown();
            executor.submit(()->{}).get(3,TimeUnit.SECONDS);
            check(calls.get()==1,"a queued upload cannot run after sending was disabled");

            calls.set(0); CountDownLatch firstCycle=new CountDownLatch(1), finishFirst=new CountDownLatch(1), followUp=new CountDownLatch(1);
            enabled.set(true);
            loop=new ResilientRelayLoop(executor,enabled::get,()->{
                if(calls.incrementAndGet()==1) {
                    firstCycle.countDown();
                    try { finishFirst.await(3,TimeUnit.SECONDS); }
                    catch(InterruptedException interrupted){Thread.currentThread().interrupt();throw new RuntimeException(interrupted);}
                } else followUp.countDown();
            },()->failures.incrementAndGet(),3600000L);
            loop.start();check(firstCycle.await(3,TimeUnit.SECONDS),"first request occupies relay");
            for(int i=0;i<100;i++)loop.start();
            finishFirst.countDown();check(followUp.await(3,TimeUnit.SECONDS),"notification received during HTTP gets an immediate follow-up without waiting an hour");
            enabled.set(false);loop.stop();executor.submit(()->{}).get(3,TimeUnit.SECONDS);
            check(calls.get()==2,"one hundred notifications coalesce into one serial follow-up");

            long elapsed=5000L, wall=1700000000000L;
            long idle=RelayRecoveryPolicy.jobDeadline(elapsed,wall,Long.MAX_VALUE);
            check(idle==elapsed+15*60*1000L,"idle recovery retains a persisted fifteen-minute safety job");
            long urgent=RelayRecoveryPolicy.jobDeadline(elapsed,wall,wall-1000L);
            check(urgent==elapsed+10000L,"an overdue queued upload requests the bounded ten-second recovery");
            check(RelayRecoveryPolicy.replaceJob(idle,urgent),"new queued receipt pulls the existing idle job forward");
            check(!RelayRecoveryPolicy.replaceJob(urgent,RelayRecoveryPolicy.jobDeadline(elapsed+5000L,wall+5000L,wall-1000L)),"healthy ticks never push the urgent job back");
            long afterIdle=elapsed+16*60*1000L;
            check(!RelayRecoveryPolicy.replaceJob(idle,RelayRecoveryPolicy.jobDeadline(afterIdle,wall+16*60*1000L,Long.MAX_VALUE)),"sixteen minutes of Android job deferral cannot continually reset its minimum latency");
            check(RelayRecoveryPolicy.jobDeadline(elapsed,wall,wall+60*60*1000L)==idle,"retry deadline remains capped after a wall-clock change");
            check(RelayRecoveryPolicy.replaceJob(0,idle),"a process restart replaces an unknown old persisted schedule once");
            check(RelayRecoveryPolicy.armWatchdog(0),"enabled relay initially arms an independent watchdog");
            check(!RelayRecoveryPolicy.armWatchdog(elapsed+120000L),"repeated network and notification ticks coalesce into one watchdog");
            check(!RelayRecoveryPolicy.armWatchdog(elapsed-16*60*1000L),"an idle-delayed watchdog is retained rather than moved forward by a screen wake");
            check(RelayRecoveryPolicy.mayRebind(1000,0),"a known disconnected listener can request its existing grant");
            check(!RelayRecoveryPolicy.mayRebind(30999,1000),"listener requests cannot loop during a network burst");
            check(RelayRecoveryPolicy.mayRebind(31000,1000),"a still-disconnected listener can recover after the cooldown");

            ScheduledThreadPoolExecutor clock=new ScheduledThreadPoolExecutor(1);
            clock.setRemoveOnCancelPolicy(true);
            try {
                FakeConnection stalled=new FakeConnection();
                RelayRequestDeadline deadline=new RelayRequestDeadline(stalled,clock,20L);
                check(stalled.disconnected.await(3,TimeUnit.SECONDS),"an independent clock disconnects a stalled network request while the relay executor is busy");
                check(deadline.expired(),"network deadline is available as non-sensitive diagnostic evidence");
                deadline.close();
                check(stalled.disconnects.get()==1,"deadline recovery does not submit a second financial upload");
                FakeConnection acknowledged=new FakeConnection();
                deadline=new RelayRequestDeadline(acknowledged,clock,1000L);deadline.close();
                clock.submit(()->{}).get(3,TimeUnit.SECONDS);
                check(clock.getQueue().isEmpty(),"successful ACK cancels its unused request timer");
                check(acknowledged.disconnects.get()==0,"completed requests are not disconnected by an obsolete timer");
            } finally { clock.shutdownNow(); }
            System.out.println("PASS: "+checks+" background relay lifecycle checks");
        } finally { executor.shutdownNow(); }
    }
    private static void check(boolean value,String reason){checks++;if(!value)throw new AssertionError(reason);}
    private static final class FakeConnection extends HttpURLConnection {
        final CountDownLatch disconnected=new CountDownLatch(1);
        final AtomicInteger disconnects=new AtomicInteger();
        FakeConnection() throws Exception { super(new URL("https://relay.invalid/notification")); }
        @Override public void disconnect(){disconnects.incrementAndGet();disconnected.countDown();}
        @Override public boolean usingProxy(){return false;}
        @Override public void connect(){throw new AssertionError("No real network connection is used by lifecycle tests");}
    }
}
