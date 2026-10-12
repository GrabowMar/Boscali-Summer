using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Copies the services into StrFeeds, the one place the board reads ModuleServices. Every lookup is lazy and per tick, never at install, so a
    /// service registered after the first tick is picked up on the next one, and a client simply has no host-only service. A copy loop with no
    /// rules: the unknowns stay unknown here (a missing service is Available false, an age of -1, a null snapshot) and the builder prints them
    /// as dashes. The sim's check deltas against the host snapshot are the proof of this file, since no harness sees live services.
    /// </summary>
    internal static class StrFeedsReader
    {
        private const float SlowSeconds = 1f;

        internal static void Read(ref StrFeeds into, StrViewHub hub, float now)
        {
            bool slow = now >= hub.NextSlowAt;
            if (slow) hub.NextSlowAt = now + SlowSeconds;

            into.IsHost = GameAccess.IsServer();
            into.LocalId = GameManager.GetLocalPlayer<Player>(out Player local) ? PlayerIdentity.Of(local) : PlayerIdentity.None;
            bool haveFaction = GameAccess.TryGetLocalFaction(out FactionHQ hq);
            string faction = haveFaction && hq.faction != null ? hq.faction.factionName ?? "" : "";
            into.HasOwn = GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null;
            if (into.HasOwn)
            {
                GlobalPosition at = aircraft.GlobalPosition();
                into.OwnX = at.x;
                into.OwnZ = at.z;
            }
            else into.OwnX = into.OwnZ = 0f;

            ReadWar(ref into, hub, now, slow, ref faction);
            into.FactionCode = faction;
            ReadSorties(ref into, hub, now);
            ReadPostsAndWing(ref into, hub, slow);
            into.PickerPrompt = MapPicker.Prompt ?? "";
            if (slow) hub.KnownAirDefence = KnownAirDefence(hub, haveFaction ? hq : null);
            into.KnownAirDefence = hub.KnownAirDefence;
            into.Stamp = StrFeedStamp.Of(in into);
        }

        private static void ReadWar(ref StrFeeds into, StrViewHub hub, float now, bool slow, ref string faction)
        {
            if (!ModuleServices.TryGet(out ITheaterWarView war))
            {
                // No Living Front module: the staff is off for this pilot, and nothing about the war is known.
                into.WarAvailable = false;
                into.WarState = TheaterViewState.StaffOff;
                into.War = null;
                into.WarAgeSeconds = into.WarSincePublished = -1f;
                into.WaitSeconds = 0f;
                into.WarCanCommand = into.WarPending = false;
                into.WarLast = null;
                ReadOwnCensus(ref into, now);
                hub.WaitingSince = -1f;
                return;
            }
            if (slow) war.Refresh();
            into.WarAvailable = war.Available;
            into.WarState = war.State;
            TheaterWarSnapshot snapshot = war.Snapshot;
            // A snapshot of another faction is not ours: drop it, and the board waits for the next report of the faction we are in.
            if (snapshot != null && faction.Length > 0 && !string.IsNullOrEmpty(snapshot.Faction) &&
                !string.Equals(snapshot.Faction, faction, StringComparison.Ordinal)) snapshot = null;
            into.War = snapshot;
            if (faction.Length == 0 && snapshot != null) faction = snapshot.Faction ?? "";
            into.WarAgeSeconds = war.SnapshotAgeSeconds;
            into.WarSincePublished = war.SecondsSincePublished;
            into.WarCanCommand = war.CanCommand;
            into.WarPending = war.CommandPending;
            into.WarLast = war.LastCommand;

            // The stopwatch of a first report that has not come, on the presentation clock, from the first time the state reads waiting.
            if (war.State == TheaterViewState.WaitingForHost)
            {
                if (hub.WaitingSince < 0f) hub.WaitingSince = now;
                into.WaitSeconds = now - hub.WaitingSince;
            }
            else
            {
                hub.WaitingSince = -1f;
                into.WaitSeconds = 0f;
            }

            // The snapshot's census wins when the host measured it (the same rule CommandManager.TickCensus applies to the ticker's bag); without one
            // a host reads the census CommandManager measured for its own faction, and a client reads it as not measured.
            if (snapshot != null && snapshot.Census.Known)
            {
                into.Census = snapshot.Census;
                into.CensusAgeSeconds = into.WarAgeSeconds;
            }
            else ReadOwnCensus(ref into, now);
        }

        // CommandManager.LastCensus is what TickCensus last applied: the host's own TryMeasure result (every 10 s, so the board adds no scan) or
        // TheaterCensus.Unmeasured, with LastCensusAt -1 until a measured one exists. Its age is unscaled now minus that stamp.
        private static void ReadOwnCensus(ref StrFeeds into, float now)
        {
            CommandManager command = CommandManager.Active;
            if (into.IsHost && command != null && command.LastCensus.Known && command.LastCensusAt >= 0f)
            {
                into.Census = command.LastCensus;
                into.CensusAgeSeconds = Math.Max(0f, now - command.LastCensusAt);
            }
            else
            {
                into.Census = TheaterCensus.Unmeasured;
                into.CensusAgeSeconds = -1f;
            }
        }

        private static void ReadSorties(ref StrFeeds into, StrViewHub hub, float now)
        {
            if (!ModuleServices.TryGet(out ISecondaryObjectivesView sorties))
            {
                into.SortiesAvailable = into.SortiesFresh = into.SortieActionPending = false;
                into.Sorties = Array.Empty<SecondaryObjectiveView>();
                into.SortieActiveLimit = 0;
                return;
            }
            into.SortiesAvailable = true;
            into.SortiesFresh = sorties.IsFresh;
            into.SortieActionPending = sorties.IsActionPending;
            into.Sorties = sorties.Objectives ?? (System.Collections.Generic.IReadOnlyList<SecondaryObjectiveView>)Array.Empty<SecondaryObjectiveView>();
            into.SortieActiveLimit = sorties.ActiveLimit;
            hub.NoteSortieAnswer(sorties.ActionResult, now);
        }

        // Enemy command posts arrive with the high command repair (the trimmed view with global positions); until then the feed is not available,
        // which the board prints as such rather than as an empty list. The wing line is host only (RefreshWing).
        private static void ReadPostsAndWing(ref StrFeeds into, StrViewHub hub, bool slow)
        {
            into.PostsAvailable = false;
            into.PostsAgeSeconds = -1f;
            into.Posts = hub.PostBuffer;
            into.PostCount = 0;

            if (slow) RefreshWing(into.IsHost, into.Sorties, hub);
            into.Wing = hub.WingBuffer;
            into.WingCount = hub.WingCount;
        }

        // Refreshed at 1 Hz for the tied cards the board can show (at most five): the order source says what SEND WING would do, the orders
        // say why not and how the last tasking is going. A client has neither service.
        private static void RefreshWing(bool isHost, System.Collections.Generic.IReadOnlyList<SecondaryObjectiveView> cards, StrViewHub hub)
        {
            hub.WingCount = 0;
            hub.TaskedSortieId = -1;
            if (!isHost || cards == null || !ModuleServices.TryGet(out ISortieWingOrderSource source) ||
                !ModuleServices.TryGet(out IWingSortieOrders orders)) return;
            for (int i = 0; i < cards.Count && hub.WingCount < hub.WingBuffer.Length; i++)
            {
                SecondaryObjectiveView card = cards[i];
                if (card == null || card.Link == null || !card.Link.Tied || !card.Link.OperationLive) continue;
                if (!source.TryGetWingOrder(card.Id, out WingSortieOrder order)) continue;
                var row = new StrWingIn { SortieId = card.Id, Intent = order.Intent, Refusal = orders.Refusal(in order), State = WingSortieState.None, Words = "" };
                if (orders.TryGetStatus(card.Id, out WingSortieStatus status))
                {
                    row.State = status.State;
                    row.Words = status.Words;
                    if (status.State == WingSortieState.Tasked || status.State == WingSortieState.OnStation) hub.TaskedSortieId = card.Id;
                }
                hub.WingBuffer[hub.WingCount++] = row;
            }
        }

        // Distinct enemy air-defence sites the faction knows, or -1 until its threat picture is ready (a dash, never a zero).
        private static int KnownAirDefence(StrViewHub hub, FactionHQ hq)
        {
            if (hq == null || !ModuleServices.TryGet(out IThreatPicture picture)) return -1;
            int observer = hq.GetInstanceID();
            bool ready = picture.IsReady(observer);
            if (ready && hub.Rings == null) hub.Rings = new AirDefenceRing[ThreatPictureLimits.MaximumRings];
            int count = ready ? picture.CopyAirDefence(observer, hub.Rings) : 0;
            return TheaterReadout.KnownSites(ready, hub.Rings, count);
        }
    }
}
