using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Server-side cruise strike records and the leg driver (Tier-2 tasking, §6.1). One record
    /// per accepted cruise salvo: its live missiles, queued legs and the final target. Legs go
    /// out one at a time through each missile's vanilla <c>UnitCommand.SetDestination</c>; the
    /// seeker's own arrival check advances the chain, and its native half-terminal-range refusal
    /// stays the backstop behind the mod-side pre-check. Terminal range and the dive/skim
    /// profile are read once per strike off the live seeker; an unreadable seeker fails closed
    /// (direct flight still works, legs are refused).
    /// </summary>
    internal sealed class CruiseTasking
    {
        internal sealed class Strike
        {
            public int RequestId;
            public ulong RequesterId;
            public FactionHQ Owner;
            public GlobalPosition Target;
            public readonly List<Missile> Missiles = new List<Missile>(8);
            public readonly List<GlobalPosition> Legs = new List<GlobalPosition>(StrikeBallistics.MaxLegs);
            public float TerminalRange;
            public bool Dive;
            public float LastLegAt = float.MinValue;
            public bool Driving;
        }

        /// <summary>Most strikes tracked at once; past it new strikes fly untasked.</summary>
        public const int MaxStrikes = 16;

        private const float LegRateSeconds = 1f;
        private const float GuidanceSettleSeconds = 2f;
        private const float LegTimeoutSeconds = 180f;

        private static readonly FieldInfo TerminalRangeField =
            typeof(OpticalSeekerCruiseMissile).GetField("terminalRange", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TopAttackField =
            typeof(OpticalSeekerCruiseMissile).GetField("topAttack", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TopAttackAmountField =
            typeof(TopAttack).GetField("Amount");

        private readonly Dictionary<int, Strike> strikes = new Dictionary<int, Strike>();
        private readonly Action<IEnumerator> run;
        private readonly Action<Strike, SupportResult> changed;

        /// <param name="run">Runs driver coroutines (the host).</param>
        /// <param name="changed">Broadcasts the strike's legs after every event, refusals too.</param>
        public CruiseTasking(Action<IEnumerator> run, Action<Strike, SupportResult> changed)
        {
            this.run = run;
            this.changed = changed;
        }

        public void Clear() => strikes.Clear();

        public Strike Find(int requestId)
        {
            strikes.TryGetValue(requestId, out Strike strike);
            return strike;
        }

        /// <summary>Records a fresh salvo; null when the board is full (the strike flies direct).</summary>
        public Strike Track(int requestId, ulong requesterId, FactionHQ owner, GlobalPosition target, Missile first)
        {
            if (first == null) return null;
            if (!strikes.TryGetValue(requestId, out Strike strike))
            {
                if (strikes.Count >= MaxStrikes) return null;
                strike = new Strike { RequestId = requestId, RequesterId = requesterId, Owner = owner, Target = target };
                strikes[requestId] = strike;
            }
            if (!strike.Missiles.Contains(first) && strike.Missiles.Count < 8) strike.Missiles.Add(first);
            if (strike.TerminalRange <= 0f) ReadSeeker(strike, first);
            return strike;
        }

        public void AddMissile(int requestId, Missile missile)
        {
            if (missile == null) return;
            if (!strikes.TryGetValue(requestId, out Strike strike)) return;
            if (!strike.Missiles.Contains(missile) && strike.Missiles.Count < 8) strike.Missiles.Add(missile);
        }

        /// <summary>Queues a leg after the seeker-rule pre-check; starts the driver on accept.</summary>
        public SupportResult QueueLeg(Strike strike, GlobalPosition leg, float now, float maxRange)
        {
            SupportResult result = TryQueue(strike, leg, now, maxRange);
            if (changed != null) changed(strike, result);
            return result;
        }

        private SupportResult TryQueue(Strike strike, GlobalPosition leg, float now, float maxRange)
        {
            if (strike == null) return SupportResult.SpawnFailed;
            if (!float.IsFinite(leg.x) || !float.IsFinite(leg.z)) return SupportResult.InvalidTarget;
            if (now - strike.LastLegAt < LegRateSeconds) return SupportResult.Busy;
            if (strike.Legs.Count >= StrikeBallistics.MaxLegs) return SupportResult.NoStock;
            if (!FirstMissile(strike, out Missile missile)) return SupportResult.SpawnFailed;
            Vector3 at = missile.transform.position;
            Vector3 mark = leg.ToLocalPosition();
            Vector3 goal = strike.Target.ToLocalPosition();
            if (Vector3.Distance(mark, goal) > maxRange) return SupportResult.InvalidTarget;
            float toWaypoint = Vector3.Distance(at, mark);
            float toTarget = Vector3.Distance(at, goal);
            if (StrikeBallistics.LegRefusedBySeeker(toWaypoint, toTarget, strike.TerminalRange))
                return SupportResult.InvalidTarget;
            strike.Legs.Add(leg);
            strike.LastLegAt = now;
            if (!strike.Driving && run != null) run(Drive(strike));
            return SupportResult.Accepted;
        }

        public void ClearLegs(Strike strike)
        {
            if (strike == null) return;
            strike.Legs.Clear();
            if (changed != null) changed(strike, SupportResult.Accepted);
        }

        /// <summary>Live route time: missile through legs to target over the real top speed.</summary>
        public float RouteTti(Strike strike)
        {
            if (strike == null) return -1f;
            if (!FirstMissile(strike, out Missile missile)) return -1f;
            Vector3 at = missile.transform.position;
            Vector3 goal = strike.Target.ToLocalPosition();
            float topSpeed = missile.GetTopSpeed(at.y, goal.y);
            var route = new List<StrikeWaypoint>(StrikeBallistics.MaxWaypoints);
            route.Add(new StrikeWaypoint(at.x, at.z));
            for (int i = 0; i < strike.Legs.Count && route.Count < StrikeBallistics.MaxWaypoints - 1; i++)
            {
                Vector3 leg = strike.Legs[i].ToLocalPosition();
                route.Add(new StrikeWaypoint(leg.x, leg.z));
            }
            route.Add(new StrikeWaypoint(goal.x, goal.z));
            return StrikeBallistics.MultiLegTimeOfFlight(route, topSpeed);
        }

        private IEnumerator Drive(Strike strike)
        {
            strike.Driving = true;
            try
            {
                // Legs sent before guidance starts are overwritten by the seeker; wait it out.
                float settle = SupportManager.MissionNow() + 5f;
                while (SupportManager.MissionNow() < settle)
                {
                    if (!FirstMissile(strike, out Missile young)) yield break;
                    if (young.timeSinceSpawn > GuidanceSettleSeconds) break;
                    yield return new WaitForSeconds(0.25f);
                }
                while (strike.Legs.Count > 0)
                {
                    SendToAll(strike, strike.Legs[0]);
                    float deadline = SupportManager.MissionNow() + LegTimeoutSeconds;
                    while (SupportManager.MissionNow() < deadline)
                    {
                        if (strike.Legs.Count == 0) break;
                        if (!FirstSeeker(strike, out OpticalSeekerCruiseMissile seeker)) yield break;
                        if (seeker.CheckWaypoint()) break;
                        yield return new WaitForSeconds(0.2f);
                    }
                    if (strike.Legs.Count > 0) strike.Legs.RemoveAt(0);
                    if (changed != null) changed(strike, SupportResult.Accepted);
                }
                SendToAll(strike, strike.Target);
                if (changed != null) changed(strike, SupportResult.Accepted);
            }
            finally
            {
                strike.Driving = false;
            }
        }

        private static void SendToAll(Strike strike, GlobalPosition point)
        {
            for (int i = strike.Missiles.Count - 1; i >= 0; i--)
            {
                Missile missile = strike.Missiles[i];
                if (missile == null || missile.disabled)
                {
                    strike.Missiles.RemoveAt(i);
                    continue;
                }
                UnitCommand command = missile.GetComponent<UnitCommand>();
                if (command != null) command.SetDestination(point, false);
            }
        }

        private static bool FirstMissile(Strike strike, out Missile missile)
        {
            for (int i = strike.Missiles.Count - 1; i >= 0; i--)
            {
                Missile candidate = strike.Missiles[i];
                if (candidate == null || candidate.disabled) strike.Missiles.RemoveAt(i);
            }
            missile = strike.Missiles.Count > 0 ? strike.Missiles[0] : null;
            return missile != null;
        }

        private static bool FirstSeeker(Strike strike, out OpticalSeekerCruiseMissile seeker)
        {
            seeker = null;
            if (!FirstMissile(strike, out Missile missile)) return false;
            seeker = missile.GetComponentInChildren<OpticalSeekerCruiseMissile>(true);
            return seeker != null;
        }

        private static void ReadSeeker(Strike strike, Missile missile)
        {
            try
            {
                OpticalSeekerCruiseMissile seeker = missile.GetComponentInChildren<OpticalSeekerCruiseMissile>(true);
                if (seeker == null || TerminalRangeField == null) return;
                object range = TerminalRangeField.GetValue(seeker);
                if (range is float terminal && terminal > 0f && float.IsFinite(terminal))
                    strike.TerminalRange = terminal;
                if (TopAttackField != null && TopAttackAmountField != null)
                {
                    object attack = TopAttackField.GetValue(seeker);
                    object amount = attack != null ? TopAttackAmountField.GetValue(attack) : null;
                    strike.Dive = amount is float top && top > 0f;
                }
            }
            catch (Exception)
            {
                strike.TerminalRange = 0f;
            }
        }
    }

    /// <summary>Client mirror of one strike's legs: confirmed legs, the live-read profile, route TTI.</summary>
    internal sealed class CruiseLegMirror
    {
        public readonly List<GlobalPosition> Legs = new List<GlobalPosition>(StrikeBallistics.MaxLegs);
        public bool Dive;
        public float Tti = -1f;
    }
}
