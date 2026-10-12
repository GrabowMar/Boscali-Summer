using System;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    // The stage machine of a group: where the line is, which side is ours, where each member stands, when the group halts, advances or is stalled.
    internal sealed partial class GroundFrontService
    {
        private enum Frame { Fronted, NoFront, HoldInPlace }

        private void RefreshGroup(Group group, float now)
        {
            if (!factions.TryGetValue(group.HQ, out FactionState fs)) return;
            if (group.Key == null)
            {
                // Not near any front and not chosen: vanilla's own orders stand.
                ClearDestinations(group);
                group.Approach = default;
                group.Closing = false;
                group.Eta = -1;
                return;
            }
            if (!group.Sealed && now - group.Born >= GroupFormation.JoinSeconds) group.Sealed = true;
            int axis = PincerAxisOf(group);
            if (group.Axis != axis)
            {
                group.Axis = axis;
                if (group.ReportedRally) ReportStage(group);
            }
            Frame frame = FrameOf(group, fs);
            if (frame == Frame.NoFront)
            {
                GroundProbe.FrameNoFront++;
                // The map offers no line to stage behind: say so (the hook counts NoFront) rather than inventing a place.
                ClearDestinations(group);
                group.Approach = default;
                group.Closing = false;
                group.Eta = -1;
                return;
            }
            if (frame == Frame.HoldInPlace)
            {
                GroundProbe.FrameHold++;
                if (CanDiagnose(group, "side")) Say(group, "no friendly side known (hold in place)");
                // Which side is ours is unknown: hold where we stand rather than guess and cross the line.
                foreach (Member member in group.Members) Freeze(member, now, 0f);
                group.Approach = default;
                group.Closing = false;
                group.Eta = -1;
                // A side that stays unknown is a freeze like any other: the stall release applies, or the group would hold the effort's quota for ever.
                if (FrozenClock.Step(ref group.AllFrozenSince, true, now)) MarkStalled(group, now);
                return;
            }
            if (group.Sealed && !group.ReportedRally)
            {
                group.ReportedRally = true;
                ReportStage(group);
            }
            PlaceMembers(group, now);
            GroundStage entered = group.Stage;
            StepStage(group, fs, now);
            if (group.Stage != entered) PlaceMembers(group, now);
            Progress(group, now);
        }

        /// <summary>Where the line is for this group and which side of it is ours.</summary>
        private Frame FrameOf(Group group, FactionState fs)
        {
            if (group.Defending)
            {
                // A defended objective is held at its own position, independent of the front.
                group.CenterX = group.FixX;
                group.CenterZ = group.FixZ;
                float dx = group.CentroidX - group.FixX, dz = group.CentroidZ - group.FixZ;
                float square = dx * dx + dz * dz;
                float inverse = square > 1f ? 1f / (float)Math.Sqrt(square) : 0f;
                group.FriendlyX = inverse > 0f ? dx * inverse : 0f;
                group.FriendlyZ = inverse > 0f ? dz * inverse : 1f;
                group.TangentX = group.FriendlyZ;
                group.TangentZ = -group.FriendlyX;
                return Frame.Fronted;
            }
            FrontTrace trace = fs.Trace;
            // A fix far beyond the line stages at the line point nearest to it and assaults from there.
            if (trace.Count <= 0 || territory == null || !FrontlineTactics.TrySlot(trace.Points, trace.Lengths, trace.Count,
                    group.FixX, group.FixZ, 0, out float cx, out float cz, out float tx, out float tz, StagingReachMeters))
                return Frame.NoFront;
            group.CenterX = cx + group.Axis * PincerFlankMeters * tx;
            group.CenterZ = cz + group.Axis * PincerFlankMeters * tz;
            group.TangentX = tx;
            group.TangentZ = tz;

            float nx = -tz, nz = tx;
            int factionId = group.HQ.GetInstanceID();
            float probeSign = 0f;
            // Command's control field has kilometre cells; close probes often share one cell.
            for (int probe = 450; probe <= 3600 && probeSign == 0f; probe *= 2)
            {
                bool left = territory.TryGetHoldStrength(factionId, group.CenterX + nx * probe, group.CenterZ + nz * probe, out float leftHold);
                bool right = territory.TryGetHoldStrength(factionId, group.CenterX - nx * probe, group.CenterZ - nz * probe, out float rightHold);
                if (left && right && Mathf.Abs(leftHold - rightHold) >= .05f) probeSign = leftHold > rightHold ? 1f : -1f;
            }
            float side = FrontSide.Decide(probeSign, group.HasSide, group.FriendlyX, group.FriendlyZ, nx, nz,
                group.Stage != GroundStage.Withdraw, group.CentroidX - group.CenterX, group.CentroidZ - group.CenterZ);
            if (side == 0f) return Frame.HoldInPlace;
            group.FriendlyX = nx * side;
            group.FriendlyZ = nz * side;
            group.HasSide = true;
            return Frame.Fronted;
        }

        // ---- Slots ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every member's destination for the group's current stage. The staged point is tried first, then the remembered candidate of an earlier pass
        /// (steady state: one raycast), then back toward our side and along the line, never forward. A member with no usable candidate holds where it stood.
        /// </summary>
        private void PlaceMembers(Group group, float now)
        {
            foreach (Member member in group.Members)
            {
                if ((member.Degraded || member.Marched) && now < member.RetryAt) continue;
                float lateral = LaneOffset(member.Lane);
                float x, z;
                bool exact = false;
                switch (group.Stage)
                {
                    case GroundStage.Rally:
                        x = group.CenterX + group.FriendlyX * 650f + group.TangentX * lateral;
                        z = group.CenterZ + group.FriendlyZ * 650f + group.TangentZ * lateral;
                        break;
                    case GroundStage.Line:
                        x = group.CenterX + group.FriendlyX * 180f + group.TangentX * lateral;
                        z = group.CenterZ + group.FriendlyZ * 180f + group.TangentZ * lateral;
                        break;
                    case GroundStage.Withdraw:
                        x = group.CenterX + group.FriendlyX * 1400f + group.TangentX * lateral;
                        z = group.CenterZ + group.FriendlyZ * 1400f + group.TangentZ * lateral;
                        break;
                    case GroundStage.Contact:
                        x = member.HoldX; z = member.HoldZ; exact = true;
                        break;
                    case GroundStage.Assault:
                        x = group.FixX + group.TangentX * (lateral + group.Axis * PincerFlankMeters);
                        z = group.FixZ + group.TangentZ * (lateral + group.Axis * PincerFlankMeters);
                        break;
                    default:
                        // An unset stage is never an assault: the member holds.
                        Freeze(member, now, 10f);
                        continue;
                }
                if (TryPlace(member, group, x, z, exact)) { member.Marched = false; continue; }
                if (!exact && TryMarch(member, x, z, now)) continue;
                if (!member.Degraded && CanDiagnose(group, "slot")) Say(group, "no usable slot for lane " + member.Lane + " at " + (int)x + "," + (int)z + ", group " +
                    (int)Scalar.Distance2D(group.CentroidX, group.CentroidZ, x, z) + " m away, staged point " + Terrain(x, z));
                Freeze(member, now, 10f);
            }
        }

        /// <summary>
        /// Candidates 0..8 are SlotSearch's (the staged point, then back and along the line); 9..14 are SlotPull's, along the way from the staged point toward the group, for a
        /// line that runs through water. The candidate that worked last time is tried first; a pulled one is checked against the staged point itself each time, so a line that
        /// comes back onto land pulls the group back with it.
        /// </summary>
        private static bool TryPlace(Member member, Group group, float x, float z, bool exact)
        {
            if (exact) return Ground(x, z, member);
            int first = member.SlotIndex >= 0 ? member.SlotIndex : 0;
            bool pulled = first >= SlotSearch.Candidates;
            if (pulled && TryAt(member, group, x, z, 0)) return true;
            if (TryAt(member, group, x, z, first)) return true;
            for (int index = 0; index < SlotSearch.Candidates + SlotPull.Steps; index++)
                if (index != first && !(pulled && index == 0) && TryAt(member, group, x, z, index)) return true;
            return false;
        }

        /// <summary>
        /// The staged point has no usable ground anywhere near it (a front at the water's edge): the member goes as far toward it as dry ground leads from where it stands,
        /// and asks again in 10 s. A member that cannot move even one step toward it holds as before.
        /// </summary>
        private static bool TryMarch(Member member, float x, float z, float now)
        {
            GlobalPosition from = member.Vehicle.GlobalPosition();
            float dx = x - from.x, dz = z - from.z, length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!(length > 1f)) return false;
            float ux = dx / length, uz = dz / length;
            float reach = LandMarch.Reach(length, along => Usable(from.x + ux * along, from.z + uz * along, out _));
            if (CanDiagnose(member.Group, "march")) Say(member.Group, "march " + (int)reach + " of " + (int)length + " m toward the staged point; just past it " +
                Terrain(from.x + ux * (reach + LandMarch.ShoreMarginMeters + LandMarch.StepMeters), from.z + uz * (reach + LandMarch.ShoreMarginMeters + LandMarch.StepMeters)));
            if (reach <= 0f || !Ground(from.x + ux * reach, from.z + uz * reach, member)) return false;
            member.Marched = true;
            member.SlotIndex = -1;
            member.RetryAt = now + 10f;
            GroundProbe.SlotMarches++;
            return true;
        }

        private static bool TryAt(Member member, Group group, float x, float z, int index)
        {
            float px, pz;
            if (index < SlotSearch.Candidates)
            {
                if (!SlotSearch.Offset(index, out float along, out float back)) return false;
                px = x + along * group.TangentX + back * group.FriendlyX;
                pz = z + along * group.TangentZ + back * group.FriendlyZ;
            }
            else
            {
                if (!SlotPull.Offset(index - SlotSearch.Candidates, group.CentroidX - x, group.CentroidZ - z, out float dx, out float dz)) return false;
                px = x + dx;
                pz = z + dz;
            }
            if (!Ground(px, pz, member)) return false;
            member.SlotIndex = index;
            return true;
        }

        /// <summary>A usable point: terrain under it, dry, not too steep.</summary>
        private static bool Usable(float x, float z, out RaycastHit ground)
        {
            ground = default;
            return float.IsFinite(x) && float.IsFinite(z) && PathfindingAgent.RaycastTerrain(new GlobalPosition(x, 0f, z), out ground) &&
                ground.point.y >= Datum.LocalSeaY && ground.normal.y >= .55f;
        }

        /// <summary>A usable point becomes the member's destination.</summary>
        private static bool Ground(float x, float z, Member member)
        {
            if (!Usable(x, z, out RaycastHit ground)) return false;
            member.Destination = ground.point.ToGlobalPosition();
            member.HasDestination = true;
            member.Degraded = false;
            return true;
        }

        /// <summary>
        /// The member holds where it stood, and the hook answers that fixed point, so vanilla neither charges the enemy objective nor re-paths every few seconds.
        /// The first hold point is kept while the freeze lasts.
        /// </summary>
        private static void Freeze(Member member, float now, float retryIn)
        {
            if (!member.Degraded)
            {
                member.Degraded = true;
                member.Destination = member.Vehicle.GlobalPosition();
                member.SlotIndex = -1;
                GroundProbe.SlotFallbacks++;
            }
            member.HasDestination = true;
            member.Marched = false;
            member.RetryAt = now + retryIn;
        }

        private const int DiagnosedPerKey = 3, DiagnosedPerScene = 48;
        private static readonly System.Collections.Generic.Dictionary<string, int> diagnosed = new System.Collections.Generic.Dictionary<string, int>();
        private static int diagnosedTotal;

        /// <summary>The first few slot failures and the first few side failures of each front or operation key in a scene go to the log with their coordinates, so a stuck group can be told from a bad map (one early key must not use up the allowance of the later ones).</summary>
        private static bool CanDiagnose(Group group, string topic)
        {
            string key = (group.Key ?? string.Empty) + "|" + topic;
            diagnosed.TryGetValue(key, out int seen);
            if (seen >= DiagnosedPerKey || diagnosedTotal >= DiagnosedPerScene) return false;
            diagnosed[key] = seen + 1;
            diagnosedTotal++;
            return true;
        }

        /// <summary>What the terrain says at a staged point that no member could use: no hit, below the sea, or too steep.</summary>
        private static string Terrain(float x, float z)
        {
            if (!float.IsFinite(x) || !float.IsFinite(z)) return "not a number";
            if (!PathfindingAgent.RaycastTerrain(new GlobalPosition(x, 0f, z), out RaycastHit hit)) return "no terrain hit";
            return "y " + (int)hit.point.y + " (sea " + (int)Datum.LocalSeaY + "), slope normal " + hit.normal.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Say(Group group, string what)
        {
            Plugin.Logger?.LogInfo("[THEATER OPS] FRONT GROUP " + group.Id + " — " + group.Stage.ToString().ToUpperInvariant() + ": " + what + " (centre " + (int)group.CenterX + "," +
                (int)group.CenterZ + ", fix " + (int)group.FixX + "," + (int)group.FixZ + ", key " + group.Key + ")");
        }

        private static void ClearDestinations(Group group)
        {
            foreach (Member member in group.Members) { member.HasDestination = false; member.Degraded = false; member.Marched = false; }
        }

        private static float LaneOffset(int lane)
        {
            int column = lane == 0 ? 0 : (lane + 1) / 2 * (lane % 2 == 1 ? 1 : -1);
            return column * FrontlineTactics.LaneSpacing;
        }

        // ---- Stages -----------------------------------------------------------------------------------------------------------

        private void StepStage(Group group, FactionState fs, float now)
        {
            if (group.Stage == GroundStage.Withdraw) return;
            int alive = group.Members.Count;
            if (group.Sealed && FrontlineTactics.ShouldWithdraw(alive, group.Formed))
            {
                EndContact(fs, group, now);
                SetStage(group, GroundStage.Withdraw, now);
                return;
            }
            GroundVehicle lead = group.Members[0].Vehicle;
            bool sighting = lead != null && NearSighting(fs, lead.GlobalPosition());
            if (ContactRule.Step(ref group.Contact, sighting, now))
            {
                if (group.Stage != GroundStage.Contact)
                {
                    group.BeforeContact = group.Stage;
                    group.ContactEntered = now;
                    foreach (Member member in group.Members)
                    {
                        GlobalPosition here = member.Vehicle.GlobalPosition();
                        member.HoldX = here.x;
                        member.HoldZ = here.z;
                        member.Degraded = false;
                    }
                    SetStage(group, GroundStage.Contact, now);
                }
                return;
            }
            if (group.Stage == GroundStage.Contact)
            {
                EndContact(fs, group, now);
                SetStage(group, group.BeforeContact, now);
            }
            if (group.Stage != GroundStage.Rally && group.Stage != GroundStage.Line) return;
            int ready = 0;
            foreach (Member member in group.Members)
            {
                if (!member.HasDestination || member.Degraded) continue;
                GlobalPosition here = member.Vehicle.GlobalPosition();
                if (Scalar.Distance2DSquared(here.x, here.z, member.Destination.x, member.Destination.z) < 300f * 300f) ready++;
            }
            if (group.Stage == GroundStage.Rally && group.Sealed && FrontlineTactics.ShouldAdvance(ready, alive, now - group.StageSince))
                SetStage(group, GroundStage.Line, now);
            else if (group.Stage == GroundStage.Line && group.Offensive && now - group.StageSince >= 15f &&
                     FrontlineTactics.ShouldAdvance(ready, alive, now - group.StageSince))
                SetStage(group, GroundStage.Assault, now);
        }

        private static bool NearSighting(FactionState fs, GlobalPosition at)
        {
            float reach = ContactRule.RadiusMeters * ContactRule.RadiusMeters;
            for (int i = 0; i < fs.Sightings.Count; i++)
                if (Scalar.Distance2DSquared(at.x, at.z, fs.Sightings[i].x, fs.Sightings[i].y) < reach) return true;
            return false;
        }

        /// <summary>
        /// Closing, ETA and stall, measured on the group's centroid against the mean of its staged points. A group that has not closed 150 m for 180 s (and is not within
        /// 400 m) is stalled on its key for five minutes: the staff tops the effort up from other groups and the group falls back to a front or to vanilla. Halted and
        /// withdrawing groups are not judged.
        /// </summary>
        private void Progress(Group group, float now)
        {
            if (group.Stage == GroundStage.Contact || group.Stage == GroundStage.Withdraw)
            {
                group.Closing = false;
                group.Eta = -1;
                return;
            }
            int staged = 0;
            float tx = 0f, tz = 0f;
            foreach (Member member in group.Members)
            {
                if (!member.HasDestination || member.Degraded) continue;
                tx += member.Destination.x; tz += member.Destination.z; staged++;
            }
            if (staged == 0)
            {
                // Every member is frozen: no target to close on. A freeze that lasts is a stall too.
                group.Closing = false;
                group.Eta = -1;
                if (FrozenClock.Step(ref group.AllFrozenSince, true, now)) MarkStalled(group, now);
                return;
            }
            FrozenClock.Step(ref group.AllFrozenSince, false, now);
            group.TargetX = tx / staged;
            group.TargetZ = tz / staged;
            float distance = Scalar.Distance2D(group.CentroidX, group.CentroidZ, group.TargetX, group.TargetZ);
            if (ApproachProgress.Stalled(ref group.Approach, distance, now)) MarkStalled(group, now);
            group.Closing = ApproachProgress.Closing(group.Approach, distance, now);
            group.Eta = ApproachProgress.EtaSeconds(group.Approach, distance, now);
        }

        private static void MarkStalled(Group group, float now)
        {
            bool fresh = !(group.StalledKey == group.Key && now < group.StalledUntil);
            group.StalledKey = group.Key;
            group.StalledUntil = now + StallLockSeconds;
            if (fresh) Plugin.Logger?.LogInfo("[THEATER OPS] FRONT GROUP " + group.Id + " — STALLED on " + group.Key);
        }
    }
}
