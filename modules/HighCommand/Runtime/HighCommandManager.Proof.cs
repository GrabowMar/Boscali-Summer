#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.HighCommand.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.HighCommand.Runtime
{
    // Sim readback and levers for HighCommandAutomation. Nothing here changes a rule: Confirm only records the sighting a patrol would, and Kill disables the real
    // building through the vanilla path with a recorded dealer, so the bounty chain under test is the production one.
    internal sealed partial class HighCommandManager
    {
        private FactionHQ SeatHq => GameManager.GetLocalPlayer<Player>(out Player p) && p != null ? p.HQ : null;

        internal Dictionary<string, object> ProofRead()
        {
            FactionHQ seat = SeatHq;
            int observer = FactionIndex(seat);
            var rows = new Dictionary<string, object>();
            int enemy = 0, own = 0, siteIsName = 0, positionOk = 0, mismatch = 0, claimed = 0, bountyRows = 0;
            for (int i = 0; i < viewCommanders.Count; i++)
            {
                CommanderView v = viewCommanders[i];
                if (v.IsFriendly) { own++; continue; }
                FactionCommand owner = factions[FactionOf(v.Id)];
                CommandSlot slot = owner.Tree.Find(LocalId(v.Id));
                Unit building = slot == null ? null : FindAsset(owner, slot, AssetKind.Post)?.Unit;
                // A scene object name reads like an id (airbase_desert1, Name(Clone)); a base whose own name is already a place name ("The Farm") is fine.
                if (v.Site.IndexOf('_') >= 0 || v.Site.IndexOf("(Clone)", StringComparison.Ordinal) >= 0 || v.Site.StartsWith("airbase", StringComparison.OrdinalIgnoreCase)) siteIsName++;
                if (building != null)
                {
                    GlobalPosition at = building.transform.position.ToGlobalPosition();
                    if (Mathf.Abs(at.x - v.X) < 2f && Mathf.Abs(at.z - v.Z) < 2f) positionOk++; else mismatch++;
                }
                if (v.BountyClaimed) claimed++;
                if (v.Bounty > 0) bountyRows++;
                rows["e" + enemy++] = new Dictionary<string, object>
                {
                    { "id", v.Id }, { "site", v.Site }, { "x", v.X }, { "z", v.Z }, { "bounty", v.Bounty }, { "claimed", v.BountyClaimed ? 1 : 0 },
                    { "intelAge", v.IntelAge }, { "name", v.Rank + " " + v.Name }, { "bonus", v.Bonus },
                };
            }
            CommanderView lastRow = null;
            for (int i = 0; i < viewCommanders.Count; i++) if (!viewCommanders[i].IsFriendly && viewCommanders[i].Id == LastPickId) lastRow = viewCommanders[i];
            int posts = 0, away = 0;
            for (int f = 0; f < factions.Count; f++)
                for (int s = 0; s < factions[f].Tree.Slots.Count; s++)
                {
                    if (FindAsset(factions[f], factions[f].Tree.Slots[s], AssetKind.Post) != null) posts++;
                    if (factions[f].Tree.Slots[s].Status == CommanderStatus.InTransit) away++;
                }
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "lastPick", new Dictionary<string, object> { { "id", LastPickId }, { "inFeed", lastRow != null ? 1 : 0 }, { "claimed", lastRow != null && lastRow.BountyClaimed ? 1 : 0 }, { "bounty", lastRow != null ? lastRow.Bounty : -1 } } },
                {
                    "view", new Dictionary<string, object>
                    {
                        { "available", Available ? 1 : 0 }, { "age", float.IsInfinity(AgeSeconds) ? -1f : AgeSeconds }, { "own", own }, { "enemy", enemy },
                        { "siteIsObjectName", siteIsName }, { "positionMatchesBuilding", positionOk }, { "positionMismatch", mismatch }, { "claimedRows", claimed }, { "bountyRows", bountyRows },
                        { "status", status }, { "statusLen", Status.Length }, { "rows", rows },
                    }
                },
                {
                    "host", new Dictionary<string, object>
                    {
                        { "factions", factions.Count }, { "postsStanding", posts }, { "away", away }, { "paidTotal", BountyPaidTotal },
                        { "funds", seat != null ? seat.factionFunds : -1f }, { "observer", observer },
                    }
                },
            };
        }

        /// <summary>The sighting a patrol would record: every standing post of every other faction is confirmed for the seat's faction.</summary>
        internal int ProofConfirm()
        {
            FactionHQ seat = SeatHq;
            int observer = FactionIndex(seat);
            if (observer < 0) return -1;
            int n = 0;
            float now = MissionTime;
            for (int f = 0; f < factions.Count; f++)
            {
                if (f == observer) continue;
                for (int s = 0; s < factions[f].Tree.Slots.Count; s++)
                {
                    CommandSlot slot = factions[f].Tree.Slots[s];
                    Unit building = FindAsset(factions[f], slot, AssetKind.Post)?.Unit;
                    if (building == null || building.disabled) continue;
                    GlobalPosition at = building.transform.position.ToGlobalPosition();
                    if (intel.Confirm(observer, GlobalId(f, slot.Id), now, at.x, at.y, at.z)) n++;
                }
            }
            return n;
        }

        /// <summary>Kills the standing post of an office with the seat's aircraft as the recorded dealer. away: the commander is on a convoy for the instant of the kill.</summary>
        internal Dictionary<string, object> ProofKill(int office, bool away)
        {
            var reply = new Dictionary<string, object> { { "ok", false } };
            FactionHQ seat = SeatHq;
            // -1 is the office of the last STRIKE COMMAND POST pick; -3 is the first enemy post of the staff board that is not that office and still stands.
            if (office == -1) office = LastPickId;
            else if (office == -3)
            {
                office = -1;
                for (int i = 0; i < viewCommanders.Count && office < 0; i++)
                {
                    CommanderView v = viewCommanders[i];
                    if (v.IsFriendly || v.Id == LastPickId) continue;
                    FactionCommand o = factions[FactionOf(v.Id)];
                    CommandSlot s = o.Tree.Find(LocalId(v.Id));
                    if (s != null && FindAsset(o, s, AssetKind.Post)?.Unit != null) office = v.Id;
                }
            }
            reply["office"] = office;
            if (office < 0 || FactionOf(office) >= factions.Count) { reply["error"] = "no such office"; return reply; }
            if (seat == null || !GameManager.GetLocalPlayer<Player>(out Player pilot) || pilot.Aircraft == null) { reply["error"] = "the seat has no aircraft to be the dealer"; return reply; }
            FactionCommand owner = factions[FactionOf(office)];
            CommandSlot slot = owner.Tree.Find(LocalId(office));
            AssetWatch watch = slot == null ? null : FindAsset(owner, slot, AssetKind.Post);
            if (watch?.Unit == null || watch.Unit.disabled) { reply["error"] = "that office has no standing post"; return reply; }
            Unit post = watch.Unit;
            float before = seat.factionFunds;
            int paidBefore = BountyPaidTotal;
            CommanderStatus status = slot.Status;
            if (away) slot.Status = CommanderStatus.InTransit;
            post.RecordDamage(pilot.Aircraft.persistentID, 1f);
            post.DisableUnit();
            if (assetLookup.ContainsKey(watch.InstanceId)) OnAssetDisabled(watch, post);      // the 1 Hz sweep would do the same
            if (away) slot.Status = status;
            reply["ok"] = true;
            reply["disabled"] = post.disabled ? 1 : 0;
            reply["fundsDelta"] = seat.factionFunds - before;
            reply["paid"] = BountyPaidTotal - paidBefore;
            reply["claimed"] = bounties.Claimed(seat.GetInstanceID(), office) ? 1 : 0;
            return reply;
        }
    }
}
#endif
