using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>What a peer shows for the staff: switched off, no faction, waiting, a host without the module, stale or live (annex 01 E3, E8).</summary>
    internal static class ViewStateRulesTests
    {
        public static void Run()
        {
            LocalSwitchAndFactionComeFirst();
            AHostIsLiveAtAnyAge();
            AClientWithoutASnapshotWaitsThenBlamesTheHost();
            AClientAgesAtFifteenSeconds();
            CommandResultsExpireAfterSixSeconds();
        }

        private static TheaterViewState Decide(bool enabled = true, bool faction = true, bool server = false, bool snapshot = true,
            float age = 0f, float sinceQuery = 0f, int unanswered = 0) =>
            ViewStateRules.Decide(enabled, faction, server, snapshot, age, sinceQuery, unanswered);

        private static void LocalSwitchAndFactionComeFirst()
        {
            TestAssert.Eq(Decide(enabled: false, faction: false, server: true), TheaterViewState.StaffOff, "the local switch beats everything");
            TestAssert.Eq(Decide(faction: false, server: true), TheaterViewState.NoFaction, "no faction beats the host being live");
            TestAssert.Eq(Decide(faction: false, snapshot: false, unanswered: 5, sinceQuery: 60f), TheaterViewState.NoFaction, "and beats waiting on a host");
        }

        private static void AHostIsLiveAtAnyAge()
        {
            TestAssert.Eq(Decide(server: true, age: 0f), TheaterViewState.Live, "a fresh host");
            TestAssert.Eq(Decide(server: true, age: 999f), TheaterViewState.Live, "a host never reads stale, however old its data");
            TestAssert.Eq(Decide(server: true, snapshot: false, age: -1f), TheaterViewState.Live, "a host is live before its first report; the empty reason explains the wait");
        }

        private static void AClientWithoutASnapshotWaitsThenBlamesTheHost()
        {
            TestAssert.Eq(Decide(snapshot: false, unanswered: 1, sinceQuery: 11.9f), TheaterViewState.WaitingForHost, "1 query, 11.9 s");
            TestAssert.Eq(Decide(snapshot: false, unanswered: 1, sinceQuery: 12f), TheaterViewState.WaitingForHost, "1 query, 12.0 s: one query is not enough");
            TestAssert.Eq(Decide(snapshot: false, unanswered: 2, sinceQuery: 11.9f), TheaterViewState.WaitingForHost, "2 queries, 11.9 s: not long enough");
            TestAssert.Eq(Decide(snapshot: false, unanswered: 2, sinceQuery: 12f), TheaterViewState.HostLacksModule, "2 queries, 12.0 s: the host does not have the module");
            TestAssert.Eq(Decide(snapshot: false, unanswered: 9, sinceQuery: 300f), TheaterViewState.HostLacksModule, "and stays so");
            TestAssert.Eq(Decide(snapshot: false, unanswered: 0, sinceQuery: 300f), TheaterViewState.WaitingForHost, "no query sent yet is only waiting");
            TestAssert.Eq(Decide(snapshot: false, unanswered: 2, sinceQuery: -1f), TheaterViewState.WaitingForHost, "an unstamped first query is waiting");
            TestAssert.Eq(Decide(snapshot: false, unanswered: 2, sinceQuery: float.NaN), TheaterViewState.WaitingForHost, "a NaN stamp is waiting");
        }

        private static void AClientAgesAtFifteenSeconds()
        {
            TestAssert.Eq(Decide(age: 0f), TheaterViewState.Live, "a fresh snapshot");
            TestAssert.Eq(Decide(age: 15f), TheaterViewState.Live, "15.0 s is still live");
            TestAssert.Eq(Decide(age: 15.1f), TheaterViewState.Stale, "15.1 s is stale");
            TestAssert.Eq(Decide(age: -1f), TheaterViewState.Stale, "an unknown age is not live");
            TestAssert.Eq(Decide(age: float.NaN), TheaterViewState.Stale, "a NaN age is not live");
            TestAssert.Eq(Decide(snapshot: true, unanswered: 5, sinceQuery: 99f, age: 3f), TheaterViewState.Live, "a snapshot in hand ends the waiting");
        }

        private static void CommandResultsExpireAfterSixSeconds()
        {
            TestAssert.That(ViewStateRules.CommandFresh(105.9f, 100f), "5.9 s old is fresh");
            TestAssert.That(ViewStateRules.CommandFresh(106f, 100f), "6.0 s old is still fresh");
            TestAssert.That(!ViewStateRules.CommandFresh(106.1f, 100f), "6.1 s old is gone");
            TestAssert.That(ViewStateRules.CommandFresh(100f, 100f), "a result from this very moment is fresh");
            TestAssert.That(!ViewStateRules.CommandFresh(99f, 100f), "a stamp in the future is never fresh");
            TestAssert.That(!ViewStateRules.CommandFresh(100f, float.NaN), "a NaN stamp is not fresh");
            TestAssert.That(!ViewStateRules.CommandFresh(float.NaN, 100f), "a NaN clock is not fresh");
        }
    }
}
