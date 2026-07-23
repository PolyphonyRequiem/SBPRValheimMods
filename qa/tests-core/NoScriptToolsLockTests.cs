// ADR-0009 M2 — AT-QA-NO-SCRIPTTOOLS-LOCK: the deadlock-proof obligation (§5.2).
//
// The cancelled run wedged because probes rode the in-game console and shared the
// ScriptTools / Terminal / ValBridge main-thread lock. This design removes that
// surface: the client control plane is a dedicated loopback channel with the helper's
// OWN single-slot dispatcher, driven only by the helper's OWN main-thread scheduler
// seam (IMainThreadScheduler) — it never shares a game console/ScriptTools/ValBridge
// lock.
//
// Proving "shares no lock with the console path" fully needs the live game, but the
// load-bearing structural guarantee is engine-free and asserted here two ways:
//   (1) Lock-free by construction — the dispatcher core holds NO synchronization
//       primitive field (no object monitor, Semaphore, Mutex, ReaderWriterLock, …),
//       so there is no lock object it could ever share with another subsystem. A
//       type that owns no lock cannot deadlock on one.
//   (2) Re-entrant without deadlock — a continuation posted to the helper's own
//       scheduler may itself call back into the dispatcher (offer/poll/complete)
//       and completes, demonstrating the dispatcher takes no self-lock that a nested
//       main-thread call would block on. The live non-reentry proof against the real
//       Terminal/ScriptTools lock lands with the engine-bound pump in a later slice.
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class NoScriptToolsLockTests
    {
        // Known .NET synchronization primitive types the control-plane core must NOT own —
        // owning any of these would create a lock object shareable with another subsystem.
        private static readonly Type[] SyncPrimitives =
        {
            typeof(Mutex),
            typeof(Semaphore),
            typeof(SemaphoreSlim),
            typeof(ReaderWriterLock),
            typeof(ReaderWriterLockSlim),
            typeof(SpinLock),
            typeof(ManualResetEvent),
            typeof(ManualResetEventSlim),
            typeof(AutoResetEvent),
            typeof(Monitor),
            typeof(Barrier),
            typeof(CountdownEvent),
        };

        public static readonly object[][] ControlPlaneCoreTypes =
        {
            new object[] { typeof(ControlDispatcher) },
            new object[] { typeof(DeliveringPeerState) },
            new object[] { typeof(LoopbackFrameParser) },
        };

        [Theory]
        [MemberData(nameof(ControlPlaneCoreTypes))]
        public void CoreType_OwnsNoSynchronizationPrimitive(Type t)
        {
            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var f in fields)
            {
                Assert.DoesNotContain(f.FieldType, SyncPrimitives);
                // Also reject a bare `object` field used as a `lock` target — the classic
                // shared-monitor pattern. The dispatcher deliberately holds none.
                Assert.False(f.FieldType == typeof(object),
                    $"{t.Name}.{f.Name} is a bare object (possible lock target); the core must be lock-free.");
            }
        }

        [Fact]
        public void Dispatcher_ReentrantFromSchedulerContinuation_DoesNotDeadlock()
        {
            var d = new ControlDispatcher(maxQueueDepth: 1);
            var sched = new FakeMainThreadScheduler { NowUnixMs = 1_000 };

            // First primitive goes in flight.
            Assert.Equal(SlotState.InFlight, d.Offer("a", sched.NowUnixMs, 5_000).State);

            // A continuation (as would run on the helper's OWN Update pump) completes "a"
            // and RE-ENTERS the dispatcher to offer "b". If the dispatcher took a self-lock
            // this nested call on the same (single) thread would wedge; it does not.
            sched.Post(() =>
            {
                Assert.Equal(ControlPlaneReason.None, d.Complete("a", sched.NowUnixMs, 5_000));
                Assert.Equal(SlotState.InFlight, d.Offer("b", sched.NowUnixMs, 5_000).State);
            });

            int drained = sched.Drain(); // runs the continuation on this thread
            Assert.Equal(1, drained);
            Assert.Equal("b", d.InFlightId);
            Assert.Equal(SlotState.Completed, d.Status("a")!.State);
        }

        [Fact]
        public void Scheduler_IsHelperOwnedSeam_NotAGameConsoleType()
        {
            // The only scheduling surface the dispatcher's live pump uses is IMainThreadScheduler,
            // which lives in the helper's OWN engine-free namespace — not any Terminal/ScriptTools/
            // ValBridge type. This is the structural anchor of the non-reentry claim.
            var ns = typeof(IMainThreadScheduler).Namespace;
            Assert.Equal("SBPR.QaHarness.T022.Core.ControlPlane", ns);
            Assert.DoesNotContain("Terminal", typeof(IMainThreadScheduler).FullName!);
            Assert.DoesNotContain("ScriptTools", typeof(IMainThreadScheduler).FullName!);
            Assert.DoesNotContain("ValBridge", typeof(IMainThreadScheduler).FullName!);
        }
    }
}
