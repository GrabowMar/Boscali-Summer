using System;
using UnityEngine;

using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Presentation;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>
    /// Wing's provider of the WING page's feed (<see cref="WingBoardScope"/>, HUD v8): every wingman against the local player, in the player's frame (x right, y ahead, heading up),
    /// with its slot, state word, fuel, munitions, target and the missile on it; the formation's name; the player's own order acknowledgements and autopilot. Computed on demand, once
    /// per frame at most, from the aircraft's transforms (a few vector operations per wingman); the snapshot-derived state words every fifth of a second.
    /// <list type="bullet">
    /// <item>Host and solo: the live wing (<see cref="WingService"/>): everything above.</item>
    /// <item>Client with a wing snapshot mirror (<see cref="WingNet.Mirror"/>, fresh): the rows (state, fuel, munitions, slot error) and the real positions of the synced aircraft; NO slots,
    /// targets, threats, hull or fields (the snapshot carries none of them: <see cref="WingBoardScope.Client"/>). Today the host sends its snapshot to its own local player only, so a
    /// remote client has no mirror and no feed: the page hides there.</item>
    /// </list>
    /// </summary>
    internal sealed class WingScopeFeed : IWingService
    {
        private const int Cap = 8, AckShown = 3;
        private const float SlowSeconds = 0.2f, AckWindowSeconds = 90f;

        public string Name => "Scope feed";

        private readonly WingBoardMember[] members = new WingBoardMember[Cap];
        private readonly WingBoardAck[] acks = new WingBoardAck[AckShown];
        private readonly WingMember[] refs = new WingMember[Cap];
        private readonly WingState[] states = new WingState[Cap];
        private readonly string[] names = new string[Cap], callsignFor = new string[Cap], targetNames = new string[Cap], fieldNames = new string[Cap], seekerNames = new string[Cap], seekerFor = new string[Cap];
        private readonly int[] nameNumber = new int[Cap];
        private readonly Unit[] targetFor = new Unit[Cap];
        private readonly AirbaseSample[] fieldFor = new AirbaseSample[Cap];
        private readonly float[] ackCacheId = new float[AckShown];
        private readonly string[] ackWho = new string[AckShown], ackText = new string[AckShown];
        private WingBoardScope cache;
        private ScopeProvider<WingBoardScope> provider;
        private int used, stamp = -1;
        private bool valid;
        private float nextSlow;
        private FormationDefinition shapeFor;
        private string shapeName;

        public void Activate()
        {
            provider = Read;
            ScopeFeeds.Register(provider);
            Clear();
        }

        public void Deactivate()
        {
            ScopeFeeds.Unregister(provider);
            Clear();
        }

        public void FixedTick(float dt)
        {
        }

        private void Clear()
        {
            stamp = -1;
            valid = false;
            used = 0;
            cache = default;
            nextSlow = 0f;
        }

        /// <summary>Refreshed on demand (the page reads every frame while it shows, the board's poll ten times a second): once per frame however many readers there are.</summary>
        private bool Read(out WingBoardScope data)
        {
            if (stamp != Time.frameCount)
            {
                stamp = Time.frameCount;
                Refresh();
            }
            data = cache;
            return valid;
        }

        public void Tick(float dt)
        {
        }

        private void Refresh()
        {
            valid = false;
            cache.Members = members;
            cache.Acks = acks;
            cache.Count = 0;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled) return;
            float now = Time.unscaledTime;
            bool slow = now >= nextSlow;
            if (slow) nextSlow = now + SlowSeconds;

            Vec3 at = me.GlobalPosition().ToVec3(), fwd = me.transform.forward.ToVec3().Horizontal;
            fwd = fwd.SqrLength < 0.01f ? Vec3.Forward : fwd.Normalized;
            Vec3 right = Vec3.Cross(Vec3.Up, fwd);

            WingService wing = WingService.Instance;
            if (!WingNet.ClientOnly && wing != null && wing.Members.Count > 0) Host(wing, at, fwd, right, slow);
            else if (WingNet.ClientOnly && WingNet.Mirror != null && !WingNet.Mirror.Stale(now)) Mirror(WingNet.Mirror, at, fwd, right);
            else { used = 0; return; }

            cache.Count = used;
            cache.Client = WingNet.ClientOnly;
            bool engaged = false;
            for (int i = 0; i < used; i++) engaged |= members[i].State == WingState.Engage;
            cache.Engaged = engaged;
            Acks(now);
            Autopilot();
            valid = used > 0;
        }

        // ------------------------------------------------------------------------------------------------ the live wing (host and solo)

        private void Host(WingService wing, Vec3 at, Vec3 fwd, Vec3 right, bool slow)
        {
            if (slow)
            {
                int n = 0;
                foreach (WingMember m in wing.Members)
                {
                    if (n >= Cap || m.Released || !m.Alive) continue;
                    refs[n] = m;
                    SnapshotMember s = wing.SnapshotOf(m);
                    var duty = (MemberDuty)s.Duty;
                    var flags = (SnapshotFlags)s.Flags;
                    states[n] = BoardRules.State(duty == MemberDuty.Defending, (flags & SnapshotFlags.Damaged) != 0, duty == MemberDuty.Recovering, m.Recovery != null && m.Recovery.Intent == RecoveryIntent.Refit,
                        duty == MemberDuty.Engaged, (flags & SnapshotFlags.Winchester) != 0, duty == MemberDuty.Grounded, duty == MemberDuty.Settled, (flags & SnapshotFlags.FallingBehind) != 0, StationMath.Error(s.Err10));
                    WingBoardMember row = members[n];
                    row.Number = m.Number;
                    row.Name = NameOf(n, m);
                    row.Fuel = BoardRules.Fraction(s.Fuel);
                    row.Ammo = BoardRules.Fraction(s.Ammo);
                    row.Bingo = (flags & SnapshotFlags.Bingo) != 0;
                    row.Joker = (flags & SnapshotFlags.Joker) != 0;
                    row.HasHull = true;
                    row.Hull = m.Damage.Hull;
                    members[n] = row;
                    n++;
                }
                used = n;
            }
            FormationSelection selection = wing.Selection;
            if (selection != null)
            {
                if (!ReferenceEquals(shapeFor, selection.Current)) { shapeFor = selection.Current; shapeName = selection.Current?.Name?.Trim().ToUpperInvariant(); }
                cache.Shape = shapeName;
            }
            else cache.Shape = null;

            for (int i = 0; i < used; i++)
            {
                WingMember m = refs[i];
                if (m == null || !m.Alive) continue;
                WingBoardMember row = members[i];
                row.State = states[i];
                Vec3 pos = m.Aircraft.GlobalPosition().ToVec3();
                Relative(pos - at, fwd, right, out row.X, out row.Y);
                row.HeadingDeg = Heading(m.Aircraft.transform.forward.ToVec3(), fwd, right);

                // The slot it is meant to hold, and how far it is from it (not for a member on the ground or landing).
                row.HasSlot = false;
                WingFrame frame = wing.FrameOf(m);
                if (frame != null && m.Brain.Slot >= 0 && m.Brain.Slot < frame.Count && row.State != WingState.Ground && row.State != WingState.Land)
                {
                    Vec3 slot = frame.Slots[m.Brain.Slot].Ref.Pos;
                    Relative(slot - at, fwd, right, out row.SlotX, out row.SlotY);
                    row.HasSlot = true;
                    row.SlotErrM = (slot - pos).Length;
                }

                // Fighting: the target the wing gave it.
                Unit target = m.Engaged ? m.AssignedTarget : null;
                row.HasTarget = target != null && !target.disabled;
                if (row.HasTarget)
                {
                    if (!ReferenceEquals(targetFor[i], target)) { targetFor[i] = target; targetNames[i] = UnitName(target); }
                    Vec3 rel = target.GlobalPosition().ToVec3() - at;
                    Relative(rel, fwd, right, out float tx, out float ty);
                    row.TargetBearingDeg = (float)(Math.Atan2(tx, ty) * 180.0 / Math.PI);
                    row.TargetRangeM = (target.GlobalPosition().ToVec3() - pos).Length;
                    row.TargetName = targetNames[i];
                }

                // A missile guiding on it.
                Missile missile = m.ThreatMissile;
                row.HasThreat = missile != null && !missile.disabled;
                if (row.HasThreat)
                {
                    Vec3 mpos = missile.GlobalPosition().ToVec3();
                    Relative(mpos - at, fwd, right, out row.ThreatX, out row.ThreatY);
                    Vec3 closeV = (missile.rb != null ? missile.rb.velocity.ToVec3() : Vec3.Zero) - (m.Aircraft.rb != null ? m.Aircraft.rb.velocity.ToVec3() : Vec3.Zero);
                    float range = (pos - mpos).Length, closing = Vec3.Dot(closeV, (pos - mpos).Normalized);
                    row.ThreatTtiS = closing > 1f ? Mathf.Min(99f, range / closing) : 99f;
                    if (!ReferenceEquals(seekerFor[i], m.ThreatSeekerType)) { seekerFor[i] = m.ThreatSeekerType; seekerNames[i] = string.IsNullOrEmpty(m.ThreatSeekerType) ? "MSL" : m.ThreatSeekerType.ToUpperInvariant(); }
                    row.ThreatSeeker = seekerNames[i];
                    row.ThreatReply = m.ThreatChoice == "IR" ? "FLARES" : "CHAFF";
                }

                // Going home: the field and how far it is.
                AirbaseSample field = m.Recovery?.Field?.Field;
                row.HasField = field != null && (row.State == WingState.Rtb || row.State == WingState.Refit);
                if (row.HasField)
                {
                    if (!ReferenceEquals(fieldFor[i], field)) { fieldFor[i] = field; fieldNames[i] = string.IsNullOrWhiteSpace(field.Name) ? "FIELD" : BaseName.Short(field.Name.Trim().ToUpperInvariant(), 12); }
                    row.Field = fieldNames[i];
                    row.FieldM = (field.Center - pos).Length;
                }
                members[i] = row;
            }
        }

        // ------------------------------------------------------------------------------------------------ the client's mirror

        private void Mirror(WingMirror mirror, Vec3 at, Vec3 fwd, Vec3 right)
        {
            int n = 0;
            for (int i = 0; i < mirror.Count && n < Cap; i++)
            {
                SnapshotMember s = mirror[i];
                var duty = (MemberDuty)s.Duty;
                var flags = (SnapshotFlags)s.Flags;
                if (!UnitRegistry.TryGetUnit(new PersistentID { Id = s.Id }, out Unit unit) || !(unit is Aircraft aircraft) || aircraft.disabled) continue;
                WingBoardMember row = default;
                row.Number = s.Slot + 2;
                if (nameNumber[n] != row.Number || names[n] == null) { nameNumber[n] = row.Number; names[n] = "#" + row.Number; callsignFor[n] = null; }
                row.Name = names[n];
                row.State = BoardRules.State(duty == MemberDuty.Defending, (flags & SnapshotFlags.Damaged) != 0, duty == MemberDuty.Recovering, false, duty == MemberDuty.Engaged,
                    (flags & SnapshotFlags.Winchester) != 0, duty == MemberDuty.Grounded, duty == MemberDuty.Settled, (flags & SnapshotFlags.FallingBehind) != 0, StationMath.Error(s.Err10));
                Vec3 pos = aircraft.GlobalPosition().ToVec3();
                Relative(pos - at, fwd, right, out row.X, out row.Y);
                row.HeadingDeg = Heading(aircraft.transform.forward.ToVec3(), fwd, right);
                row.SlotErrM = StationMath.Error(s.Err10);
                row.Fuel = BoardRules.Fraction(s.Fuel);
                row.Ammo = BoardRules.Fraction(s.Ammo);
                row.Bingo = (flags & SnapshotFlags.Bingo) != 0;
                row.Joker = (flags & SnapshotFlags.Joker) != 0;
                members[n++] = row;
            }
            used = n;
            cache.Shape = null;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// <summary>The player's frame: x right of the nose, y ahead (metres, heading up).</summary>
        private static void Relative(Vec3 v, Vec3 fwd, Vec3 right, out float x, out float y)
        {
            x = Vec3.Dot(v, right);
            y = Vec3.Dot(v, fwd);
        }

        /// <summary>A heading relative to the player's nose, compass degrees clockwise.</summary>
        private static float Heading(Vec3 forward, Vec3 fwd, Vec3 right) => (float)(Math.Atan2(Vec3.Dot(forward, right), Vec3.Dot(forward, fwd)) * 180.0 / Math.PI);

        /// <summary><c>#2</c> or <c>#2 REAPER</c> with the pilot's callsign, rebuilt only when the seat or the callsign changes.</summary>
        private string NameOf(int i, WingMember m)
        {
            string call = WingPilotRoster.Of(m)?.Callsign;
            if (nameNumber[i] != m.Number || !string.Equals(callsignFor[i], call, StringComparison.Ordinal) || names[i] == null)
            {
                nameNumber[i] = m.Number;
                callsignFor[i] = call;
                names[i] = string.IsNullOrWhiteSpace(call) ? "#" + m.Number : "#" + m.Number + " " + call.Trim().ToUpperInvariant();
            }
            return names[i];
        }

        private static string UnitName(Unit u) => (u.definition != null && !string.IsNullOrWhiteSpace(u.definition.unitName) ? u.definition.unitName : u.unitName)?.Trim().ToUpperInvariant() ?? "TARGET";

        /// <summary>The player's own newest order results (within a minute and a half), newest first; strings built only when a new one arrives.</summary>
        private void Acks(float now)
        {
            AckFeed feed = WingAcks.Feed;
            int n = 0;
            for (int i = 0; i < feed.Count && n < AckShown; i++)
            {
                AckLine l = feed.Newest(i);
                float age = now - l.Time;
                if (age > AckWindowSeconds) break;
                if (!(ackCacheId[n] == l.Time) || ackText[n] == null)
                {
                    ackCacheId[n] = l.Time;
                    ackWho[n] = l.Who;
                    ackText[n] = BoardRules.AckText(l.Accepted, l.What, l.Reason);
                }
                acks[n] = new WingBoardAck { Who = ackWho[n], Text = ackText[n], Id = l.Time, AgeS = age, Accepted = l.Accepted };
                n++;
            }
            cache.AckCount = n;
        }

        /// <summary>The player's autopilot as two short words (<c>NAV 2/5</c>, <c>ALT5000</c>): the Wing module's own gauge (<see cref="PlayerAutopilotGauge"/>), read for the page.</summary>
        private void Autopilot()
        {
            cache.HasAutopilot = PlayerAutopilotGauge.Read(out GaugeSpec g);
            cache.ApText = g.Text;
            cache.ApSub = g.Sub;
            cache.ApProgress = g.Value;
        }
    }
}
