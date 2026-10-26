using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Ares.Runtime
{
    /// <summary>The cheat switches, for this session only (never saved). The damage, fuel and blackout ones are read by the patches.</summary>
    internal static class AresCheats
    {
        public static bool God, Fuel, Ammo, Flares, NoBlackout, OneHit, Boost, LowGravity, Reveal, TimeStop, BulletTime;

        /// <summary>True when ARES keeps this unit from harm: GOD MODE on the local aircraft, or INVULN on the unit.</summary>
        public static bool Protects(Unit unit)
        {
            if (unit == null) return false;
            if (God && GameManager.IsLocalAircraft(unit)) return true;
            AresHost host = AresHost.Active;
            return host != null && host.protectedUnits.Count > 0 && host.protectedUnits.Contains(unit);
        }

        public static bool LocalDealer(PersistentID dealer) =>
            GameManager.GetLocalAircraft(out Aircraft me) && me != null && me.persistentID.Equals(dealer);
    }

    internal sealed partial class AresHost
    {
        private const float LowGravityScale = 0.3f, BulletTimeScale = 0.35f;
        private static readonly Vector3 NormalGravity = new Vector3(0f, -9.81f, 0f);

        private float nextCheatTick, nextRevealTick, nextFollowTick;

        public string SetLowGravity(bool on)
        {
            AresCheats.LowGravity = on;
            Physics.gravity = on ? NormalGravity * LowGravityScale : NormalGravity;
            return Say(on ? "LOW GRAVITY: everything with physics on this machine falls at 30 %." : "Gravity is back.");
        }

        public string SetBulletTime(bool on)
        {
            if (on && GameManager.gameState != GameState.SinglePlayer) return Say("BULLET TIME is single player only.");
            AresCheats.BulletTime = on;
            TimeScaleManager.Scale = on ? BulletTimeScale : Speeds[SpeedIndex];
            return Say(on ? "BULLET TIME." : "Time runs at speed again.");
        }

        /// <summary>TIME STOP: every unit but your aircraft frozen in place; off thaws exactly those.</summary>
        public string SetTimeStop(bool on)
        {
            if (!Host(out string refusal)) return Say(refusal);
            AresCheats.TimeStop = on;
            int n = 0;
            if (on)
            {
                foreach (Unit unit in UnitRegistry.allUnits.ToArray())
                    if (unit != null && !GameManager.IsLocalAircraft(unit) && Freeze(unit, true)) n++;
                return Say("TIME STOP: " + n + " unit(s) frozen.");
            }
            var thaw = new List<Unit>();
            foreach (KeyValuePair<Unit, Frozen> entry in frozen)
                if (entry.Value.ByTimeStop) thaw.Add(entry.Key);
            foreach (Unit unit in thaw)
                if (Thaw(unit)) n++;
            return Say("TIME RUNS: " + n + " unit(s) thawed.");
        }

        public string FixMe()
        {
            if (!Host(out string refusal)) return Say(refusal);
            if (!LocalAircraft(out Aircraft me)) return Say("You are not flying.");
            RepairUnit(me);
            RearmUnit(me);
            return Say("FIXED: repaired, rearmed and refuelled.");
        }

        public string ArmTeleportMe() =>
            Arm("TELEPORT ME", "TELEPORT ME: click where you want to be; you keep your height above the ground. Esc cancels.", false,
                (at, heading) => TeleportMe(at));

        public string TeleportMe(GlobalPosition at)
        {
            if (!LocalAircraft(out Aircraft me)) return Say("You are not flying.");
            return Say(Teleport(me, at.ToLocalPosition()) ? "TELEPORTED." : "Could not move your aircraft.");
        }

        public string AddCredit()
        {
            if (!Host(out string refusal)) return Say(refusal);
            if (!GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player)) return Say("No local player.");
            player.AddAllocation(1000f);
            return Say("+1000 CREDIT.");
        }

        private void ResetCheats()
        {
            if (AresCheats.LowGravity) Physics.gravity = NormalGravity;
            AresCheats.LowGravity = false;
            if (AresCheats.BulletTime || SpeedIndex != 2) TimeScaleManager.Scale = 1f;
            AresCheats.BulletTime = false;
            AresCheats.TimeStop = false;
            SpeedIndex = 2;
        }

        /// <summary>2 Hz: refill ammo and flares; every 2 s refresh REVEAL ALL; every 3 s re-order FOLLOW ME.</summary>
        private void TickCheats()
        {
            float now = Time.unscaledTime;
            if (now < nextCheatTick) return;
            nextCheatTick = now + 0.5f;
            if (!GameAccess.IsServer()) return;
            if ((AresCheats.Ammo || AresCheats.Flares) && LocalAircraft(out Aircraft me) && !me.disabled)
            {
                if (AresCheats.Ammo) RearmUnit(me, false);
                else if (me.countermeasureManager != null) me.countermeasureManager.Rearm(new RearmEventArgs { Rearmer = me });
            }
            if (now >= nextRevealTick)
            {
                nextRevealTick = now + 2f;
                if (AresCheats.Reveal) RevealAll();
                else TrackSpawned();
            }
            if (now >= nextFollowTick)
            {
                nextFollowTick = now + 3f;
                TickFollowers();
            }
        }

        /// <summary>Every enemy unit tracked by your side: new ones added (map icons appear), known ones refreshed so they never go stale.</summary>
        private static void RevealAll()
        {
            if (!GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player) || player.HQ == null) return;
            FactionHQ hq = player.HQ;
            int added = 0;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled || unit.NetworkHQ == hq) continue;
                TrackingInfo info = hq.GetTrackingData(unit.persistentID);
                if (info != null) info.UpdateInfo(unit.GlobalPosition());
                else if (added++ < 200) hq.RpcUpdateTrackingInfo(unit.persistentID);
            }
        }

        /// <summary>BOOST: as much thrust again as the engines make now, along the nose.</summary>
        private void FixedCheats()
        {
            if (!AresCheats.Boost || !LocalAircraft(out Aircraft me) || me.disabled) return;
            // The aircraft's own engine list: under complex physics the engine parts are unparented, so a child search finds none.
            Rigidbody body = me.CockpitRB();
            if (body == null) return;
            float thrust = 0f, rpm = 0f;
            int engines = 0;
            foreach (IEngine engine in me.engineStates)
                if (engine != null && engine.transform != null)
                {
                    thrust += engine.GetThrust();
                    rpm += engine.GetRPMRatio();
                    engines++;
                }
            // A turboprop reports no thrust (its propeller makes it): push with a third of the aircraft's weight at full RPM instead.
            if (thrust <= 0f && engines > 0) thrust = me.rb != null ? me.rb.mass * 9.81f * 0.35f * (rpm / engines) : 0f;
            if (thrust > 0f) body.AddForce(me.transform.forward * thrust, ForceMode.Force);
        }
    }

    // ---------------------------------------------------------------------- patches
    //
    // Every prefix reads a static switch first, so with the cheats off they cost one branch per call.

    [HarmonyPatch(typeof(Unit), nameof(Unit.RpcDamage))]
    internal static class AresUnitDamagePatch
    {
        private static bool Prefix(Unit __instance) => !AresCheats.Protects(__instance);
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.RpcDamage))]
    internal static class AresAircraftDamagePatch
    {
        private static bool Prefix(Aircraft __instance) => !AresCheats.Protects(__instance);
    }

    /// <summary>The server's critical-part check runs here before RpcDamage, so a protected unit is stopped here too. ONE-HIT KILLS
    /// multiplies what the local aircraft deals.</summary>
    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.TakeDamage))]
    internal static class AresPartDamagePatch
    {
        private const float OneHit = 50f;

        private static bool Prefix(UnitPart __instance, ref float pierceDamage, ref float blastDamage, ref float fireDamage, ref float impactDamage,
            PersistentID dealerID)
        {
            if (AresCheats.Protects(__instance.parentUnit)) return false;
            if (AresCheats.OneHit && AresCheats.LocalDealer(dealerID))
            {
                pierceDamage *= OneHit;
                blastDamage *= OneHit;
                fireDamage *= OneHit;
                impactDamage *= OneHit;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeGForceDamage))]
    internal static class AresGForceDamagePatch
    {
        private static bool Prefix(Pilot __instance) => !(AresCheats.God && __instance.aircraft != null && GameManager.IsLocalAircraft(__instance.aircraft));
    }

    [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeWaterDamage))]
    internal static class AresWaterDamagePatch
    {
        private static bool Prefix(Pilot __instance) => !(AresCheats.God && __instance.aircraft != null && GameManager.IsLocalAircraft(__instance.aircraft));
    }

    [HarmonyPatch(typeof(GLOC), nameof(GLOC.SimulateGLOC))]
    internal static class AresBlackoutPatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!AresCheats.NoBlackout) return true;
            __result = 1f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UseFuel))]
    internal static class AresFuelPatch
    {
        private static void Prefix(Aircraft __instance, ref float fuelDrawn)
        {
            if (AresCheats.Fuel && GameManager.IsLocalAircraft(__instance)) fuelDrawn = 0f;
        }
    }
}
