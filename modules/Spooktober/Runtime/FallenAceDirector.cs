using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Spooktober.Domain;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// Host: flies the FALLEN ACE's moods on top of the game's own combat AI four times a second (spec §4 "Advanced AI"), and every
    /// frame turns a lethal hit into a rebirth or the defeat (<see cref="FallenAce.Frame"/>). Hunt and Strike keep it in combat on
    /// the director's target (aircraft first, the host's twice as dear, else the nearest ground unit within 15 km), with a fresh
    /// track: GHOST has no radar picture of its own. Stalk breaks off and drops it off every track database, then blinks it onto
    /// the target's six; Feint blinks it sideways once per missile scare. Outside Stalk its contact still drops for 1.5 s every
    /// 6-10 s, and the rest of the time the host's side is fed its track: radars only scan listed HQs and GHOST is not one, so this
    /// feed is how the player sees and locks it. Bottomless gun and flares, slow self repair. It also hands the rad look its aircraft.
    /// </summary>
    internal sealed class FallenAceDirector : MonoBehaviour, ISceneService
    {
        private const float Step = 0.25f, FeintRearm = 10f, FlickerSeconds = 1.5f, StalkBlink = 1500f, FeintBlink = 1500f;
        private const float AirRange = 25000f, GroundRange = 15000f;

        private static readonly GameObject[] Roots = new GameObject[1];
        private static readonly AccessTools.FieldRef<Unit, Dictionary<PersistentID, float>> Credit =
            AccessTools.FieldRefAccess<Unit, Dictionary<PersistentID, float>>("damageCredit");
        private static readonly AccessTools.FieldRef<Pilot, float> PilotHitPoints = AccessTools.FieldRefAccess<Pilot, float>("hitPoints");

        private AceRules rules; // the spawn the timers below belong to
        private float nextStep, nextSecond, flickerUntil, nextFlicker, feintAt;
        private bool failed;

        private void Awake() => RadGhostWatcher.ExtraTargets = AceRoots;

        public void ResetForScene() => FallenAce.Forget();

        private void OnDestroy()
        {
            if (RadGhostWatcher.ExtraTargets == AceRoots) RadGhostWatcher.ExtraTargets = null;
            FallenAce.Forget();
        }

        private static IEnumerable<GameObject> AceRoots()
        {
            Aircraft ace = FallenAce.Current;
            Roots[0] = ace != null && !ace.disabled ? ace.gameObject : null;
            return Roots;
        }

        private void Update()
        {
            // R15 / review fix 1: GHOST cannot exist on a client. A player connecting is seen on the frame it connects, long before
            // Mirage sends it any spawned object (only once it has a character and its scene is ready), so the ace and the HQ are
            // gone before it could receive them.
            if (GhostSide.Made && GameAccess.IsServer() && GhostSide.RemoteClients()) Evict();
            if (!FallenAce.Alive || !GameAccess.IsServer()) return;
            FallenAce.Frame();
            Aircraft ace = FallenAce.Current;
            if (ace == null || ace.disabled || Time.time < nextStep) return;
            nextStep = Time.time + Step;
            try { Tick(ace, Time.time); }
            catch (Exception e)
            {
                if (!failed) Plugin.Logger.LogError("[Spooktober] FALLEN ACE director: " + e);
                failed = true;
            }
        }

        private void Tick(Aircraft ace, float now)
        {
            if (FallenAce.Rules != rules)
            {
                // A fresh spawn: its first mood timer starts now (AceRules anchors on its first Tick).
                rules = FallenAce.Rules;
                failed = false;
                nextSecond = 0f;
                nextFlicker = now + Random.Range(6f, 10f);
                flickerUntil = feintAt = float.NegativeInfinity;
            }
            FallenAce.WakeCombat(ace);
            if (now >= nextSecond)
            {
                nextSecond = now + 1f;
                Retarget(ace);
                Bottomless(ace);
            }
            Unit target = FallenAce.Target;
            var sense = new AceSense
            {
                Now = now,
                HasTarget = target != null,
                TargetDistance = target != null ? Vector3.Distance(target.transform.position, ace.transform.position) : 0f,
                HullLostInWindow = FallenAce.HullLost(now, rules.Tuning.BlinkWindow),
                SinceHit = now - FallenAce.LastHitAt,
                // Debounced: a missile still coming after a feint does not start the next one for FeintRearm seconds.
                MissileInbound = now - FallenAce.MissileAt < 3f && now - feintAt > FeintRearm,
            };
            AceMood was = rules.Mood;
            AceMood mood = rules.Tick(sense, Random.value);
            if (mood != was) Enter(ace, was, mood, target, now);
            if (rules.WantsBlink(sense))
            {
                // A solid hit: away in embers, usually onto the target's six.
                Unit anchor = FallenAce.Anchor();
                if (anchor != null) FallenAce.BlinkBehind(ace, anchor, Random.Range(rules.Tuning.BlinkMin, rules.Tuning.BlinkMax));
                else FallenAce.BlinkSideways(ace, Random.Range(rules.Tuning.BlinkMin, rules.Tuning.BlinkMax));
                FallenAce.ClearHits();
                Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE blinked away from a solid hit.");
            }
            Repair(ace, rules.RepairNow(sense, Step));
            if (mood != AceMood.Stalk && now >= nextFlicker)
            {
                flickerUntil = now + FlickerSeconds;
                nextFlicker = now + Random.Range(6f, 10f);
            }
            Tracks(ace, mood == AceMood.Stalk || now < flickerUntil);
        }

        private void Enter(Aircraft ace, AceMood was, AceMood mood, Unit target, float now)
        {
            FallenAce.MoodChanges++;
            Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE mood " + was + " -> " + mood + (target != null
                ? " (target " + target.unitName + " at " + (Vector3.Distance(target.transform.position, ace.transform.position) / 1000f).ToString("0.0") + " km)"
                : " (no target)") + ".");
            if (mood == AceMood.Feint)
            {
                feintAt = now;
                FallenAce.BlinkSideways(ace, FeintBlink);
            }
            else if (was == AceMood.Stalk && target != null && !target.disabled)
                FallenAce.BlinkBehind(ace, target, StalkBlink); // re-emerges on its six
            FallenAce.Reassess(ace); // a Stalk breaks off at once (the target patch answers none), the rest re-engage
        }

        /// <summary>Aircraft within 25 km first (1/distance, the host's own twice), else the nearest ground vehicle within 15 km,
        /// else the nearest aircraft anywhere: it never runs out of prey (without a target the AI heads home, and GHOST has none).
        /// A target that went down to the ace's guns is a kill taunt.</summary>
        private static void Retarget(Aircraft ace)
        {
            Unit was = FallenAce.Target;
            Unit pick = Pick(ace);
            if (was == pick) return;
            if (was != null && was.disabled) KillCheck(was);
            FallenAce.Target = pick;
            // A fresh track first: the AI only takes a target whose position is accurate (review fix 4: the first pass lagged ~5 s).
            if (pick != null && !pick.disabled && ace.NetworkHQ != null) Feed(ace.NetworkHQ, pick);
            FallenAce.Reassess(ace);
        }

        private static Unit Pick(Aircraft ace)
        {
            FactionHQ own = ace.NetworkHQ;
            GameManager.GetLocalAircraft(out Aircraft host);
            Vector3 at = ace.transform.position;
            Unit best = null, far = null;
            float score = 0f, farthest = float.MaxValue;
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (!Hostile(a, own) || a.HasEjected() || a.speed < 30f) continue; // parked and taxiing aircraft are not a fight
                float distance = Vector3.Distance(a.transform.position, at);
                if (distance > AirRange)
                {
                    if (distance < farthest) { farthest = distance; far = a; }
                    continue;
                }
                float value = (a == host ? 2f : 1f) / Mathf.Max(distance, 100f);
                if (value <= score) continue;
                score = value;
                best = a;
            }
            if (best != null) return best;
            float nearest = GroundRange;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (!(u is GroundVehicle) || !Hostile(u, own)) continue;
                float distance = Vector3.Distance(u.transform.position, at);
                if (distance >= nearest) continue;
                nearest = distance;
                best = u;
            }
            return best != null ? best : far;
        }

        private static bool Hostile(Unit u, FactionHQ own) => u != null && !u.disabled && u.NetworkHQ != null && u.NetworkHQ != own;

        /// <summary>The ace's lives together dealt the most damage to <paramref name="victim"/>: the game credits the kill to it.</summary>
        private static void KillCheck(Unit victim)
        {
            Dictionary<PersistentID, float> credit = Credit(victim);
            if (credit == null) return;
            float mine = 0f, top = 0f;
            foreach (KeyValuePair<PersistentID, float> entry in credit)
            {
                if (FallenAce.Ids.Contains(entry.Key)) mine += entry.Value;
                else top = Mathf.Max(top, entry.Value);
            }
            if (!(mine > 0f) || mine < top) return;
            FallenAce.Kills++;
            Plugin.Logger.LogInfo("[Spooktober] FALLEN ACE kill: " + victim.unitName + ".");
            if (FallenAce.Kills % 2 == 1) FallenAce.Taunt("kill1", "ONE MORE FOR THE COLD.");
            else FallenAce.Taunt("kill2", "THEY NEVER CHECK THEIR SIX.");
        }

        /// <summary>The GHOST side tracks the target (the AI only guns a track seen in the last 4 s); the others lose the ace while it
        /// is hidden, unless a missile of theirs is homing on it, and the host's side is fed its track otherwise. Both through the
        /// game's own sighting and forget (a bare dictionary edit made the next sighting throw, Task 8).</summary>
        private static void Tracks(Aircraft ace, bool hidden)
        {
            FactionHQ own = ace.NetworkHQ;
            Unit target = FallenAce.Target;
            if (own != null && target != null && !target.disabled && FallenAce.Rules.Mood != AceMood.Stalk) Feed(own, target);
            GameManager.GetLocalHQ(out FactionHQ mine);
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null || hq == own) continue;
                // A side with a missile homing on the ace keeps (and gets) its track: ARHSeeker.SlowChecks reads it unchecked (runs 3
                // and 5: NREs), and a missile fired in a flicker would otherwise fly on without one.
                if (FallenAce.Homing(hq, Time.time) || (!hidden && hq == mine)) Feed(hq, ace);
                else if (hidden) hq.DeregisterTrackedUnit(ace);
            }
        }

        private static void Feed(FactionHQ hq, Unit unit)
        {
            TrackingInfo track = hq.GetTrackingData(unit.persistentID);
            if (track == null) hq.RpcUpdateTrackingInfo(unit.persistentID);
            else track.UpdateInfo(unit.GlobalPosition());
        }

        /// <summary>Once a second: the gun topped up to its full load and the flares refilled.</summary>
        private static void Bottomless(Aircraft ace)
        {
            foreach (WeaponStation station in ace.weaponStations)
                if (station?.WeaponInfo != null && station.WeaponInfo.gun && station.Ammo < station.FullAmmo)
                {
                    FallenAce.Rounds += station.FullAmmo - station.Ammo;
                    station.Rearm(station.FullAmmo - station.Ammo);
                    FallenAce.Refills++;
                }
            CountermeasureManager countermeasures = ace.countermeasureManager;
            if (countermeasures != null && countermeasures.GetFlareAmmoProportion() < 1f)
                countermeasures.Rearm(new RearmEventArgs { Rearmer = ace });
        }

        /// <summary>Gives every attached part and every living pilot <paramref name="fraction"/> of its hit points back (both run 0-100).</summary>
        private static void Repair(Aircraft ace, float fraction)
        {
            if (!(fraction > 0f)) return;
            if (ace.partLookup != null)
                foreach (UnitPart part in ace.partLookup)
                    if (part != null && !part.IsDetached() && part.hitPoints < 100f)
                        part.hitPoints = Mathf.Min(100f, part.hitPoints + fraction * 100f);
            if (ace.pilots != null)
                foreach (Pilot pilot in ace.pilots)
                    if (pilot != null && !pilot.dead)
                        PilotHitPoints(pilot) = Mathf.Min(100f, PilotHitPoints(pilot) + fraction * 100f);
        }

        /// <summary>A remote player joined: the ace leaves (no kill, no defeat) and GHOST with it, announced once.</summary>
        private static void Evict()
        {
            bool flying = FallenAce.Alive;
            if (flying) FallenAce.Dismiss(); // takes GHOST with it
            else FallenAce.Retire();
            Plugin.Logger.LogWarning("[Spooktober] FALLEN ACE left: a remote player joined (GHOST is single player only for now).");
            if (flying) FallenAce.Announce("FALLEN ACE LEFT: A PLAYER JOINED");
        }
    }
}
