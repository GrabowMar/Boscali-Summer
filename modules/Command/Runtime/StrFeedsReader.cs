using System;
using System.Text;
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
            ReadPosts(ref into, hub, slow);
            ReadSupply(ref into, hub, now, slow, local, haveFaction ? hq : null);
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

        // Enemy command posts: the confirmed rows of the high command view, copied as they are (global metres, display names). A board that has not
        // arrived or has gone stale is not available, which the board prints as such rather than as an empty list.
        private static void ReadPosts(ref StrFeeds into, StrViewHub hub, bool slow)
        {
            into.PostsAvailable = false;
            into.PostsAgeSeconds = -1f;
            into.Posts = hub.PostBuffer;
            into.PostCount = 0;
            into.StaffPresent = false;
            into.StaffStatus = "";
            into.Officers = hub.OfficerBuffer;
            into.OfficerCount = 0;
            if (ModuleServices.TryGet(out IHighCommandView staff))
            {
                into.StaffPresent = true;
                into.StaffStatus = staff.Status ?? "";
            }
            if (staff != null && staff.Available)
            {
                into.PostsAvailable = true;
                into.PostsAgeSeconds = staff.AgeSeconds;
                System.Collections.Generic.IReadOnlyList<CommanderView> rows = staff.Commanders;
                for (int i = 0; i < rows.Count; i++)
                {
                    CommanderView row = rows[i];
                    if (row == null) continue;
                    // The view lists only enemy posts this faction has confirmed, so every row it carries is Confirmed; a post nobody confirmed never reaches the panel.
                    if (into.OfficerCount < hub.OfficerBuffer.Length)
                        hub.OfficerBuffer[into.OfficerCount++] = new StrOfficerIn
                        {
                            Id = row.Id, ParentId = row.ParentId, Tier = row.Tier, Own = row.IsFriendly, Alert = row.Alert, Confirmed = true, Name = row.Name, Rank = row.Rank, Role = row.Role,
                            Site = row.Site, Trait = row.Bonus, IntelAge = row.IntelAge, X = row.X, Z = row.Z, Portrait = row.Portrait,
                        };
                    if (row.IsFriendly || into.PostCount >= hub.PostBuffer.Length) continue;
                    hub.PostBuffer[into.PostCount++] = new StrPostIn
                    {
                        Id = row.Id, Bounty = row.Bounty, Name = row.Rank + " " + row.Name, Rank = row.Rank, Role = row.Role, Site = row.Site, Trait = row.Bonus,
                        IntelAge = row.IntelAge, X = row.X, Z = row.Z, BountyCapped = row.BountyClaimed,
                    };
                }
            }
        }

        // The game's own contribute menu, read the way ContributeToFaction reads it: the pilot's funds, the mission's convoy list and prices locally (the
        // list at 1 Hz, in the game's order, so a row's index is its purchase index), the cooldowns from the host through the hub. They count down here
        // between the host's answers, and one it has not answered yet is -1.
        private static void ReadSupply(ref StrFeeds into, StrViewHub hub, float now, bool slow, Player local, FactionHQ hq)
        {
            into.Convoys = hub.ConvoyBuffer;
            if (local == null || hq == null || hq.faction == null || local.HQ != hq)
            {
                into.SupplyKnown = into.DonationForbidden = false;
                into.Allocation = 0f;
                into.DonateCooldown = -1f;
                into.ConvoyCount = hub.ConvoyCount = 0;
                return;
            }
            into.SupplyKnown = true;
            into.DonationForbidden = hq.preventDonation;
            into.Allocation = local.Allocation;
            if (slow)
            {
                System.Collections.Generic.List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
                int count = groups == null ? 0 : Math.Min(groups.Count, hub.ConvoyBuffer.Length);
                for (int i = 0; i < count; i++)
                {
                    Faction.ConvoyGroup group = groups[i];
                    ref StrConvoyIn row = ref hub.ConvoyBuffer[i];
                    string name = group != null ? group.Name ?? "" : "";
                    if (row.Composition == null || !string.Equals(row.Name, name, StringComparison.Ordinal))
                    {
                        row.Name = name;
                        row.Composition = Composition(group);
                    }
                    row.Cost = ConvoyCost(group);
                }
                hub.ConvoyCount = count;
                hub.AskSupply(local, count);
            }
            into.ConvoyCount = hub.ConvoyCount;
            for (int i = 0; i < hub.ConvoyCount; i++) hub.ConvoyBuffer[i].Cooldown = Left(hub.ConvoyReadyAt[i], now);
            into.DonateCooldown = Left(hub.DonateReadyAt, now);
        }

        private static float Left(float readyAt, float now) => float.IsNaN(readyAt) ? -1f : Math.Max(0f, readyAt - now);

        // The game's own price (unit value plus its ammunition). It reads the unit prefabs, so a broken mission entry throws: that group reads as unpriced.
        private static float ConvoyCost(Faction.ConvoyGroup group)
        {
            if (group == null) return float.NaN;
            try { return group.GetCost(); }
            catch (Exception) { return float.NaN; }
        }

        // "4x LCV25 · 2x AFV8", the line the game's menu shows on hover.
        private static string Composition(Faction.ConvoyGroup group)
        {
            if (group == null || group.Constituents == null) return "";
            var text = new StringBuilder(64);
            foreach (Faction.ConvoyUnit unit in group.Constituents)
            {
                if (unit == null || unit.Type == null) continue;
                if (text.Length > 0) text.Append(StrWords.Dot);
                text.Append(unit.Count).Append("x ").Append(unit.Type.unitName);
            }
            return text.ToString();
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
