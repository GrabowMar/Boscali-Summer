using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    // What the rest of the mod and the proof read: the hook's answer, the reason steering runs or not, and the measured report. Nothing here assigns.
    internal sealed partial class GroundFrontService
    {
        /// <summary>A vehicle counts as steered while the hook answered it this recently, seconds.</summary>
        private const float SteeredSeconds = 8f;
        /// <summary>A vehicle is on its slot within this of its staged point, metres.</summary>
        private const float OnSlotMeters = 300f;

        /// <summary>
        /// Patch-side read inside vanilla's per-vehicle destination poll: one dictionary lookup against what the last pass decided. Never assigns or enrols.
        /// False hands vanilla's own answer back: not a member, a stale pass (more than 6 s old), a group with no key, or a group with no line to stage behind.
        /// A member whose slot failed answers its frozen hold point, never false.
        /// </summary>
        internal bool TryGetDestination(GroundVehicle vehicle, out GlobalPosition destination)
        {
            destination = default;
            if (!adapterOn || vehicle == null || !members.TryGetValue(vehicle, out Member member)) return false;
            Group group = member.Group;
            if (!ReferenceEquals(vehicle.NetworkHQ, group.HQ)) return false;
            GroundProbe.AdvanceEnrolled++;
            float now = Time.timeSinceLevelLoad;
            if (now - lastPass > StaleSeconds) { GroundProbe.AdvanceStale++; return false; }
            if (group.Key == null) { GroundProbe.AdvanceUnassigned++; return false; }
            if (!member.HasDestination) { GroundProbe.AdvanceNoFront++; return false; }
            destination = member.Destination;
            member.AnsweredAt = now;
            member.Answer = destination;
            GroundProbe.AdvanceReplaced++;
            return true;
        }

        /// <summary>Eligible, not support, not slung, and not in a group stalled on the faction's current operation key (it cannot carry the order, so the staff does not count it).</summary>
        internal bool CanCarry(GroundVehicle vehicle)
        {
            if (!Eligible(vehicle)) return false;
            if (!members.TryGetValue(vehicle, out Member member)) return true;
            Group group = member.Group;
            return !(group.StalledKey != null && Time.timeSinceLevelLoad < group.StalledUntil &&
                     factions.TryGetValue(group.HQ, out FactionState fs) && fs.OpKey == group.StalledKey);
        }

        /// <summary>Raw per-pass reason a faction's steering runs or not; Pending for an unknown HQ. The engine debounces it over two reviews.</summary>
        internal GroundReason ReasonFor(FactionHQ hq)
        {
            if (hq == null || !factions.TryGetValue(hq, out FactionState fs)) return GroundReason.Pending;
            return GroundReasonRule.Decide(GameAccess.IsServer(), NavigateObjectives != null && CommandedDestination != null,
                settings != null && settings.FrontlineTacticsEnabled.Value, HasRtsCommander(), fs.Eligible, fs.Trace.Count);
        }

        /// <summary>Eligible vehicles seen in the last finished sweep; -1 until the first sweep ends.</summary>
        internal int EligibleCount(FactionHQ hq) => hq != null && factions.TryGetValue(hq, out FactionState fs) ? fs.Eligible : -1;

        /// <summary>Traces copied for the faction; -1 until the first copy.</summary>
        internal int TraceCount(FactionHQ hq) => hq != null && factions.TryGetValue(hq, out FactionState fs) ? fs.Trace.Count : -1;

        private FactionState StateByName(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return null;
            for (int i = 0; i < factionHqs.Count; i++)
                if (factionHqs[i].faction != null && factionHqs[i].faction.factionName == faction && factions.TryGetValue(factionHqs[i], out FactionState fs)) return fs;
            return null;
        }

        /// <summary>
        /// The effort's ground report, measured from the vehicles: the groups on the effort for this key, how many vehicles they hold, which stage they are in, which
        /// are closing, how many vehicles the hook answered lately and how many stand on their slot. True with Reason != Ready and every number -1 when steering is not
        /// running, so the engine can say why; false for an unknown faction.
        /// </summary>
        internal bool TryReport(string faction, string key, out GroundOpReport report)
        {
            report = default;
            FactionState fs = StateByName(faction);
            if (fs == null) return false;
            GroundReason reason = ReasonFor(fs.HQ);
            if (reason != GroundReason.Ready) { report = GroundOpReport.Unavailable(reason); return true; }
            float now = Time.timeSinceLevelLoad;
            report = new GroundOpReport { Reason = GroundReason.Ready, EtaSeconds = -1, NearestToFixMeters = -1f };
            foreach (Group group in groups)
            {
                if (group.HQ != fs.HQ) continue;
                if (group.StalledKey != null && group.StalledKey == key && now < group.StalledUntil) report.Stalled++;
                if (!group.OnEffort || group.Key != key) continue;
                report.Groups++;
                report.Vehicles += group.Members.Count;
                report.Formed += group.Formed;
                switch (group.Stage)
                {
                    case GroundStage.Rally: report.Rally++; break;
                    case GroundStage.Line: report.Line++; break;
                    case GroundStage.Assault: report.Assault++; break;
                    case GroundStage.Contact: report.Contact++; break;
                    case GroundStage.Withdraw: report.Withdraw++; break;
                }
                if (group.Closing) report.Moving++;
                if (group.Eta >= 0 && (report.EtaSeconds < 0 || group.Eta < report.EtaSeconds)) report.EtaSeconds = group.Eta;
                foreach (Member member in group.Members)
                {
                    GlobalPosition here = member.Vehicle.GlobalPosition();
                    if (member.AnsweredAt >= 0f && now - member.AnsweredAt <= SteeredSeconds) report.Steered++;
                    if (member.HasDestination && !member.Degraded &&
                        Scalar.Distance2DSquared(here.x, here.z, member.Destination.x, member.Destination.z) <= OnSlotMeters * OnSlotMeters) report.OnSlot++;
                    float away = Scalar.Distance2D(here.x, here.z, group.FixX, group.FixZ);
                    if (report.NearestToFixMeters < 0f || away < report.NearestToFixMeters) report.NearestToFixMeters = away;
                }
            }
            return true;
        }

        /// <summary>Up to 8 groups on the effort for this key, nearest the fix first; returns the count.</summary>
        internal int CopyMarks(string faction, string key, GroundMark[] into)
        {
            FactionState fs = StateByName(faction);
            if (fs == null || into == null || into.Length == 0) return 0;
            int count = 0;
            var near = new float[into.Length];
            foreach (Group group in groups)
            {
                if (group.HQ != fs.HQ || !group.OnEffort || group.Key != key) continue;
                float distance = Scalar.Distance2DSquared(group.CentroidX, group.CentroidZ, group.FixX, group.FixZ);
                var mark = new GroundMark(group.CentroidX, group.CentroidZ, group.Stage, (byte)Math.Min(group.Members.Count, 6));
                int at = count;
                while (at > 0 && near[at - 1] > distance) at--;
                if (at >= into.Length) continue;
                int last = Math.Min(count, into.Length - 1);
                for (int i = last; i > at; i--) { into[i] = into[i - 1]; near[i] = near[i - 1]; }
                into[at] = mark; near[at] = distance;
                if (count < into.Length) count++;
            }
            return count;
        }

        // ---- Proof and report reads: they clear into and append ---------------------------------------------------------

        internal int CopyGroups(FactionHQ hq, List<GroundGroupReport> into)
        {
            into.Clear();
            if (hq == null) return 0;
            float now = Time.timeSinceLevelLoad;
            factions.TryGetValue(hq, out FactionState fs);
            foreach (Group group in groups)
            {
                if (group.HQ != hq) continue;
                bool usable = group.Stage != GroundStage.Withdraw && !(fs != null && fs.OpKey != null && group.StalledKey == fs.OpKey && now < group.StalledUntil);
                into.Add(new GroundGroupReport(group.Id, group.Key, group.Stage, group.OnEffort, group.FromDepot, usable, group.Members.Count, group.Formed,
                    group.CentroidX, group.CentroidZ, group.TargetX, group.TargetZ, group.FixX, group.FixZ,
                    now - group.StageSince, now - group.Born, group.FirstOnEffortAfter));
            }
            return into.Count;
        }

        internal int CopyMembers(FactionHQ hq, List<GroundMemberReport> into)
        {
            into.Clear();
            if (hq == null) return 0;
            float now = Time.timeSinceLevelLoad;
            foreach (Group group in groups)
            {
                if (group.HQ != hq) continue;
                foreach (Member member in group.Members)
                {
                    bool staged = member.HasDestination && !member.Degraded;
                    into.Add(new GroundMemberReport(member.Vehicle, group.Id, group.OnEffort, staged, member.Destination.x, member.Destination.z,
                        member.AnsweredAt >= 0f ? now - member.AnsweredAt : -1f, member.Answer.x, member.Answer.z, IsSupport(member.Vehicle), now - group.StageSince));
                }
            }
            return into.Count;
        }

        /// <summary>The faction's maxima and tallies; a Contact stay still running counts at its length so far.</summary>
        internal GroundFactionStats StatsOf(FactionHQ hq)
        {
            var stats = new GroundFactionStats();
            if (hq == null || !factions.TryGetValue(hq, out FactionState fs)) return stats;
            float now = Time.timeSinceLevelLoad;
            stats.MaxContactDwell = fs.MaxContactDwell;
            foreach (Group group in groups)
                if (group.HQ == hq && group.ContactEntered > 0f) stats.MaxContactDwell = Math.Max(stats.MaxContactDwell, now - group.ContactEntered);
            stats.Rekeys = fs.Rekeys;
            stats.EffortSwaps = fs.EffortSwaps;
            stats.Ungrouped = fs.Ungrouped;
            stats.SupportSeen = fs.SupportSeen;
            stats.SupportNavigating = fs.SupportNavigating;
            return stats;
        }
    }
}
