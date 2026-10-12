using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The client handshake (spec 9.4, 9.5): the guards in order, the held message that waits for the capture, and the clock rule for a moving key.</summary>
    internal static class SyncGateTests
    {
        public static void Run()
        {
            GuardsInOrder();
            ClockRuleTable();
            PendingKeepsTheNewestOnce();
            AfterTheCaptureTheBufferIsReplayedOnce();
        }

        private static void GuardsInOrder()
        {
            TestAssert.That(SyncGate.Decide(true, true, true, true, 100.0, 100u) == SyncDecision.Ignore, "a server never applies a sync");
            TestAssert.That(SyncGate.Decide(true, false, false, false, 0.0, 0u) == SyncDecision.Ignore, "not even before its own capture");
            TestAssert.That(SyncGate.Decide(false, false, true, true, 100.0, 100u) == SyncDecision.Buffer, "a client before its capture buffers, moving or held");
            TestAssert.That(SyncGate.Decide(false, false, false, false, 0.0, 0u) == SyncDecision.Buffer, "even with no clock yet");
            TestAssert.That(SyncGate.Decide(false, true, true, true, 100.0, 100u) == SyncDecision.Adopt, "a captured client with a matching clock adopts");
        }

        private static void ClockRuleTable()
        {
            TestAssert.That(SyncGate.Decide(false, true, true, true, 5000.0, 5000u) == SyncDecision.Adopt, "same clock");
            TestAssert.That(SyncGate.Decide(false, true, true, true, 5000.0, 5010u) == SyncDecision.Adopt && SyncGate.Decide(false, true, true, true, 5010.0, 5000u) == SyncDecision.Adopt, "10 s apart either way is inside the rule");
            TestAssert.That(SyncGate.Decide(false, true, true, true, 5000.0, 5011u) == SyncDecision.Ignore, "11 s ahead is refused");
            TestAssert.That(SyncGate.Decide(false, true, true, true, 5000.0, 15000u) == SyncDecision.Ignore && SyncGate.Decide(false, true, true, true, 15000.0, 5000u) == SyncDecision.Ignore, "+-1e4 s is refused");
            TestAssert.That(SyncGate.Decide(false, true, true, false, 0.0, 5000u) == SyncDecision.Ignore, "a moving key with no local clock is refused");
            TestAssert.That(SyncGate.Decide(false, true, false, false, 0.0, 5000u) == SyncDecision.Adopt && SyncGate.Decide(false, true, false, true, 99999.0, 5000u) == SyncDecision.Adopt, "a held key needs no clock check");
        }

        private struct Message { public int Id; }

        private static void PendingKeepsTheNewestOnce()
        {
            var pending = new PendingSync<Message>();
            TestAssert.That(!pending.Has && !pending.TryTake(out _), "empty at the start");
            pending.Hold(new Message { Id = 1 });
            pending.Hold(new Message { Id = 2 });
            TestAssert.That(pending.Has && pending.TryTake(out Message got) && got.Id == 2, "the newest message wins");
            TestAssert.That(!pending.Has && !pending.TryTake(out _), "and it is handed out once");
            pending.Hold(new Message { Id = 3 });
            pending.Clear();
            TestAssert.That(!pending.Has && !pending.TryTake(out _), "a scene reset drops it");
        }

        /// <summary>The manager's order: a message arrives before the capture (Buffer), the capture replays it, and the replay is decided again with the captured flag set.</summary>
        private static void AfterTheCaptureTheBufferIsReplayedOnce()
        {
            var pending = new PendingSync<Message>();
            bool captured = false;
            int adopted = 0;
            void Receive(Message m)
            {
                SyncDecision d = SyncGate.Decide(false, captured, false, true, 100.0, 100u);
                if (d == SyncDecision.Buffer) pending.Hold(m);
                else if (d == SyncDecision.Adopt) adopted++;
            }
            Receive(new Message { Id = 1 });
            Receive(new Message { Id = 2 });
            TestAssert.That(adopted == 0 && pending.Has, "nothing is adopted before the capture");
            captured = true;
            if (pending.TryTake(out Message replay)) Receive(replay);
            TestAssert.That(adopted == 1 && !pending.Has, "the capture replays exactly the newest message, once");
        }
    }
}
