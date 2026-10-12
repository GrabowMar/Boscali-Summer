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
                group.HasFront = false;
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
            group.HasFront = frame == Frame.Fronted;
            if (frame == Frame.NoFront)
            {
                // The map offers no line to stage behind: say so (the hook counts NoFront) rather than inventing a place.
                ClearDestinations(group);
                group.Approach = default;
                group.Closing = false;
                group.Eta = -1;
                return;
            }
            if (frame == Frame.HoldInPlace)
            {
                // Which side is ours is unknown: hold where we stand rather than guess and cross the line.
                foreach (Member member in group.Members) Freeze(member, now, 0f);
                group.Approach = default;
                group.Closing = false;
                group.Eta = -1;
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
                if (member.Degraded && now < member.RetryAt) continue;
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
                if (!TryPlace(member, group, x, z, exact)) Freeze(member, now, 10f);
            }
        }

        private static bool TryPlace(Member member, Group group, float x, float z, bool exact)
        {
            if (exact) return Ground(x, z, member);
            int first = member.SlotIndex >= 0 ? member.SlotIndex : 0;
            for (int k = -1; k < SlotSearch.Candidates; k++)
            {
                int index = k < 0 ? first : k;
                if (k >= 0 && index == first) continue;
                if (!SlotSearch.Offset(index, out float along, out float back)) continue;
                if (!Ground(x + along * group.TangentX + back * group.FriendlyX, z + along * group.TangentZ + back * group.FriendlyZ, member)) continue;
                member.SlotIndex = index;
                return true;
            }
            return false;
        }

        /// <summary>A usable point: dry, not too steep. Writes the member's destination on success.</summary>
        private static bool Ground(float x, float z, Member member)
        {
            if (!float.IsFinite(x) || !float.IsFinite(z)) return false;
            if (!PathfindingAgent.RaycastTerrain(new GlobalPosition(x, 0f, z), out RaycastHit ground) ||
                ground.point.y < Datum.LocalSeaY || ground.normal.y < .55f) return false;
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
            member.RetryAt = now + retryIn;
        }

        private static void ClearDestinations(Group group)
        {
            foreach (Member member in group.Members) { member.HasDestination = false; member.Degraded = false; }
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
                if (group.AllFrozenSince <= 0f) group.AllFrozenSince = now;
                else if (now - group.AllFrozenSince >= AllFrozenStallSeconds) MarkStalled(group, now);
                return;
            }
            group.AllFrozenSince = 0f;
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
