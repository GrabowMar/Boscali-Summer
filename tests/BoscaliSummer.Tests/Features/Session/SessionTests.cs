using System.Collections.Generic;
using BoscaliSummer.Modules.Session.Domain;

namespace BoscaliSummer.Tests.Features.Session
{
    internal static class SessionTests
    {
        public static void Run()
        {
            LedgerAppliesOnlyHostOwnedKeysAndRestoresTheOriginals();
            LedgerKeepsTheFirstOriginalAcrossRepeatedHostValues();
            ChunksStayWithinTheWireLimits();
        }

        private static void LedgerAppliesOnlyHostOwnedKeysAndRestoresTheOriginals()
        {
            var local = new Dictionary<string, string> { { "Support/CostMultiplier", "1" }, { "Squad/PilotLives", "Respawning" } };
            string Read(string key) => local.TryGetValue(key, out string value) ? value : null;
            void Write(string key, string value) => local[key] = value;
            var ledger = new HostValueLedger();

            TestAssert.That(!ledger.Active, "a fresh ledger holds no host values");
            TestAssert.That(ledger.Apply("Support/CostMultiplier", "1.5", Read, Write), "a different host value is applied");
            TestAssert.That(!ledger.Apply("Squad/PilotLives", "Respawning", Read, Write), "an equal host value changes nothing");
            TestAssert.That(!ledger.Apply("Hud/Anchor", "3", Read, Write), "a key the client does not treat as host-owned is ignored");
            TestAssert.That(!local.ContainsKey("Hud/Anchor"), "the host cannot create a client setting");
            TestAssert.That(local["Support/CostMultiplier"] == "1.5", "the host value is in force during the session");
            TestAssert.That(ledger.Active && ledger.Count == 2, "both host-owned keys are remembered, changed or not");

            ledger.Restore(Write);
            TestAssert.That(!ledger.Active, "restoring ends the session");
            TestAssert.That(local["Support/CostMultiplier"] == "1" && local["Squad/PilotLives"] == "Respawning",
                "the player's own values come back");
        }

        private static void LedgerKeepsTheFirstOriginalAcrossRepeatedHostValues()
        {
            var local = new Dictionary<string, string> { { "Comms/PingSeconds", "60" } };
            string Read(string key) => local.TryGetValue(key, out string value) ? value : null;
            void Write(string key, string value) => local[key] = value;
            var ledger = new HostValueLedger();

            ledger.Apply("Comms/PingSeconds", "30", Read, Write);
            ledger.Apply("Comms/PingSeconds", "45", Read, Write);
            TestAssert.That(local["Comms/PingSeconds"] == "45", "a later host change replaces the earlier one");
            ledger.Restore(Write);
            TestAssert.That(local["Comms/PingSeconds"] == "60", "restore returns the value from before the session, not a host value");
            TestAssert.That(!ledger.Apply(null, "1", Read, Write) && !ledger.Apply("Comms/PingSeconds", null, Read, Write),
                "missing keys or values are ignored");
        }

        private static void ChunksStayWithinTheWireLimits()
        {
            var entries = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < 50; i++) entries.Add(new KeyValuePair<string, string>("S/K" + i, i.ToString()));
            entries.Add(new KeyValuePair<string, string>("S/Long", new string('x', HostValueChunks.MaximumValueLength + 1)));
            entries.Add(new KeyValuePair<string, string>(new string('k', HostValueChunks.MaximumKeyLength + 1), "1"));

            List<KeyValuePair<string[], string[]>> chunks = HostValueChunks.Split(entries);
            int total = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                TestAssert.That(chunks[i].Key.Length == chunks[i].Value.Length, "keys and values stay parallel");
                TestAssert.That(chunks[i].Key.Length <= HostValueChunks.MaximumEntries, "no chunk exceeds the entry cap");
                total += chunks[i].Key.Length;
            }
            TestAssert.That(chunks.Count == 3 && total == 50, "oversized entries are dropped whole, the rest all travel");
            TestAssert.That(HostValueChunks.Split(new List<KeyValuePair<string, string>>()).Count == 1,
                "an empty snapshot still sends one (complete) message");
        }
    }
}
