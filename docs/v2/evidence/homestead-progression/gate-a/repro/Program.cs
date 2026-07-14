using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace GateAHarness
{
    // Independent Gate-A verification harness (T003). Link-compiles the SHIPPED T002 slice and
    // drives it through REAL out-of-process death: a child fsyncs a durable boundary then SIGKILLs
    // ITS OWN pid (no managed unwind, no finally, no graceful close) — genuine process death, the
    // thing T002's in-process CrashAfter injector could not prove. A fresh process then recovers
    // from the fsync'd journal only.
    internal static class Program
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);
        private const int SIGKILL = 9;

        // Crash injector: the store already fsync'd inside Append() BEFORE AfterBoundary fires, so
        // a hard SIGKILL here leaves exactly the target boundary durable on disk.
        private sealed class RealProcessDeathAt : ICrashInjector
        {
            private readonly ReceiptBoundary _target;
            public RealProcessDeathAt(ReceiptBoundary target) { _target = target; }
            public void AfterBoundary(ReceiptBoundary boundary)
            {
                if (boundary == _target)
                {
                    Console.Out.Write("CHILD_FSYNCED_BOUNDARY=" + (int)boundary);
                    Console.Out.Flush();
                    kill(Process.GetCurrentProcess().Id, SIGKILL); // real death, no unwind
                    Environment.FailFast("SIGKILL did not take"); // unreachable
                }
            }
        }

        private static OperationReceiptStore Store(string journal, out InMemoryMirroredStoneApStore s, out InMemoryCharacterApStore c)
        {
            s = new InMemoryMirroredStoneApStore();
            c = new InMemoryCharacterApStore();
            return new OperationReceiptStore(journal, s, c);
        }

        private static readonly AuthoritativePrincipal Owner =
            new AuthoritativePrincipal(new AccountId("acct-1"), new CharacterId("char-1"), "plat-1");

        private static int Main(string[] args)
        {
            if (args.Length == 0) { Console.Error.WriteLine("need mode"); return 2; }
            switch (args[0])
            {
                case "child-crash":
                    return ChildCrash(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture));
                case "recover":
                    return Recover(args[1], args[2]);
                case "race-child":
                    return RaceChild(args[1], args[2], long.Parse(args[3], CultureInfo.InvariantCulture));
                case "boot-balance":
                    return BootBalance(args[1]);
                default:
                    Console.Error.WriteLine("unknown mode " + args[0]); return 2;
            }
        }

        // Submit one op, then SIGKILL self right after the durable boundary N.
        private static int ChildCrash(string journal, string opId, int boundary)
        {
            var store = Store(journal, out _, out _);
            store.SubmitFoundationalAp(new OperationId(opId), Stone(), Owner, "evi",
                new RealProcessDeathAt((ReceiptBoundary)boundary));
            Console.Out.Write("CHILD_SURVIVED"); // only if boundary never hit
            return 0;
        }

        // Fresh process: recover from journal only, resubmit same op, print re-derived balances.
        private static int Recover(string journal, string opId)
        {
            var store = Store(journal, out var s, out var c);
            var recovery = new ReceiptRecovery(store);
            var pre = recovery.Inspect(opId);
            var result = store.SubmitFoundationalAp(new OperationId(opId), Stone(), Owner, "evi");
            Console.Out.WriteLine("PRE_STATUS=" + pre.Status);
            Console.Out.WriteLine("PRE_MIRRORED=" + pre.MirroredStoneAp + " PRE_PERSONAL=" + pre.PersonalAp + " PRE_CUM=" + pre.CumulativeAp);
            Console.Out.WriteLine("OUTCOME=" + result.Outcome);
            Console.Out.WriteLine("MIRRORED=" + s.GetMirroredStoneAp(Stone()));
            Console.Out.WriteLine("PERSONAL=" + c.GetPersonalAp(Owner.Account, Owner.Character, Stone()));
            Console.Out.WriteLine("CUMULATIVE=" + c.GetCumulativeAp(Owner.Account, Owner.Character, Stone()));
            Console.Out.WriteLine("RECEIPT=" + result.ReceiptId);
            Console.Out.WriteLine("STONE_REV=" + s.GetStoneRevision(Stone()));
            return 0;
        }

        // A separate OS process acting as a "client" that constructs a FRESH server over the shared
        // journal and commits a distinct op expecting the given stone revision. Probes whether CAS
        // is sound when the in-memory aggregate is not rehydrated from the journal on boot.
        private static int RaceChild(string journal, string opId, long expectedStoneRev)
        {
            var store = Store(journal, out var s, out var c);
            var resolver = new PrincipalResolver(p => p);
            // resolver maps platform->account identity, so the resolved account == platform id "plat-1".
            var resolvedAccount = new AccountId("plat-1");
            var authorizer = new PreconfiguredTestAuthorizer().Allow(resolvedAccount, Stone());
            var pipeline = new ProgressionCommandPipeline(resolver, store, authorizer);
            var adapter = new FoundationalPlacementAdapter();
            var evidence = new FoundationalPlacementEvidence(new OperationId(opId), Stone(),
                "piece-" + opId, "prov-" + opId, true, true, "v1");
            var admission = adapter.Admit(evidence, new AuthenticatedConnection("plat-1", "char-1"),
                new ClaimedPrincipal("plat-1", "char-1"), expectedStoneRev, expectedStoneRev);
            var r = pipeline.Handle(admission.Command);
            Console.Out.WriteLine(opId + " OUTCOME=" + r.Outcome + " CODE=" + r.ResultCode + " STONE_REV=" + r.StoneRevision);
            return 0;
        }

        private static StoneId Stone() => StoneId.FromHostZone(new WorldId("uid:harness"), 5, 5);

        // Boot a FRESH server over a journal that already has committed ops, and report the balances
        // the in-memory aggregate exposes WITHOUT resubmitting anything. If projections aren't
        // rehydrated from the journal at boot, this prints 0 despite durable committed AP.
        private static int BootBalance(string journal)
        {
            var store = Store(journal, out var s, out var c);
            Console.Out.WriteLine("BOOT_MIRRORED=" + s.GetMirroredStoneAp(Stone()));
            Console.Out.WriteLine("BOOT_STONE_REV=" + s.GetStoneRevision(Stone()));
            Console.Out.WriteLine("BOOT_PERSONAL=" + c.GetPersonalAp(Owner.Account, Owner.Character, Stone()));
            var recovery = new ReceiptRecovery(store);
            int jMir = 0;
            foreach (var op in store.DurableOperationIds())
                jMir += recovery.Inspect(op).MirroredStoneAp;
            Console.Out.WriteLine("JOURNAL_TRUTH_MIRRORED=" + jMir);
            return 0;
        }
    }
}
