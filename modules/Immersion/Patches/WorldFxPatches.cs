using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BoscaliSummer.Modules.Immersion.Audio;
using BoscaliSummer.Modules.Immersion.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Patches
{
    /// <summary>Near-miss crack: the step's start position is taken before the trace moves the bullet.</summary>
    [HarmonyPatch(typeof(BulletSim.Bullet), nameof(BulletSim.Bullet.TrajectoryTrace))]
    internal static class NearMissCrackPatch
    {
        private static void Prefix(BulletSim.Bullet __instance, out GlobalPosition __state) => __state = __instance.position;

        // A muzzle is passed only on the spawn step, which starts at the gun: no crack there.
        private static void Postfix(BulletSim.Bullet __instance, GlobalPosition __state, Transform muzzle, WeaponInfo info, Unit owner,
            bool hasCamera, GlobalPosition cameraPos)
        {
            if (!WorldFx.CrackArmed || muzzle != null || !hasCamera) return;
            NearMissCrack.Consider(__state, __instance.position, __instance.velocity, info, owner, cameraPos);
        }
    }

    /// <summary>
    /// Vanilla already delays explosion audio at the speed of sound and dulls it with distance; in the cockpit the canopy also
    /// takes the top end off. Vanilla bypasses the source's effects on some explosions, so the filter is switched back on.
    /// </summary>
    [HarmonyPatch(typeof(ExplosionAudioManager.ManagedExplosion), nameof(ExplosionAudioManager.ManagedExplosion.Play))]
    internal static class ExplosionMufflePatch
    {
        private const float CanopyCutoffHz = 3200f;

        private static void Postfix(AudioSource ___audioSource, AudioLowPassFilter ___filter)
        {
            if (!WorldFx.MuffleArmed || !ImmersionManager.IsCockpitActive || ___audioSource == null || ___filter == null) return;
            ___audioSource.bypassEffects = false;
            ___filter.cutoffFrequency = Mathf.Min(___filter.cutoffFrequency, CanopyCutoffHz);
        }
    }

    /// <summary>
    /// Parked AI (host side, where AI states run): navigation lights off and the crew out of the cockpit until the aircraft
    /// leaves the parked state, when the crew returns and the lights follow the gear again.
    /// </summary>
    internal static class ParkedAircraft
    {
        private static readonly ConditionalWeakTable<PilotParkedState, Aircraft> Owners = new ConditionalWeakTable<PilotParkedState, Aircraft>();
        private static readonly ConditionalWeakTable<Aircraft, object> Quiet = new ConditionalWeakTable<Aircraft, object>();
        internal static readonly Type NavLightType = AccessTools.Inner(typeof(NavLights), "NavLight");
        private static readonly FieldInfo LightsField = AccessTools.Field(typeof(NavLights), "navLights");
        private static readonly MethodInfo ToggleMethod = NavLightType != null ? AccessTools.Method(NavLightType, "Toggle") : null;

        internal static bool IsQuiet(Aircraft aircraft) => aircraft != null && Quiet.TryGetValue(aircraft, out _);

        internal static void Enter(PilotParkedState state, Aircraft aircraft)
        {
            if (aircraft == null) return;
            Owners.Remove(state);
            Owners.Add(state, aircraft);
            Quiet.Remove(aircraft);
            Quiet.Add(aircraft, aircraft);
            SetLights(aircraft, false);
            SetCrew(aircraft, false);
        }

        internal static void Leave(PilotParkedState state)
        {
            if (!Owners.TryGetValue(state, out Aircraft aircraft) || aircraft == null) return;
            Owners.Remove(state);
            if (!Quiet.Remove(aircraft)) return;
            SetCrew(aircraft, true);
            LandingGear.GearState gear = aircraft.gearState;
            SetLights(aircraft, gear == LandingGear.GearState.LockedExtended || gear == LandingGear.GearState.Extending);
        }

        // Event-rare (park and start-up only), so the component search and reflection calls are fine here.
        private static void SetLights(Aircraft aircraft, bool on)
        {
            if (LightsField == null || ToggleMethod == null) return;
            var args = new object[] { on };
            foreach (NavLights lights in aircraft.GetComponentsInChildren<NavLights>(true))
                if (LightsField.GetValue(lights) is Array array)
                    foreach (object light in array) if (light != null) ToggleMethod.Invoke(light, args);
        }

        private static void SetCrew(Aircraft aircraft, bool visible)
        {
            Pilot[] pilots = aircraft.pilots;
            for (int i = 0; pilots != null && i < pilots.Length; i++) pilots[i]?.TogglePilotVisibility(visible);
        }
    }

    [HarmonyPatch(typeof(PilotParkedState), nameof(PilotParkedState.EnterState))]
    internal static class ParkedEnterPatch
    {
        private static void Postfix(PilotParkedState __instance, Pilot pilot)
        {
            if (WorldFx.ParkedArmed && pilot != null) ParkedAircraft.Enter(__instance, pilot.aircraft);
        }
    }

    [HarmonyPatch(typeof(PilotParkedState), nameof(PilotParkedState.LeaveState))]
    internal static class ParkedLeavePatch
    {
        private static void Postfix(PilotParkedState __instance) => ParkedAircraft.Leave(__instance);
    }

    /// <summary>Gear events would switch a parked aircraft's lights back on; hold them off while it is parked.</summary>
    [HarmonyPatch]
    internal static class ParkedNavLightPatch
    {
        private static bool Prepare() => ParkedAircraft.NavLightType != null;
        private static MethodBase TargetMethod() => AccessTools.Method(ParkedAircraft.NavLightType, "Toggle");

        private static void Prefix(ref bool enabled, Aircraft ___aircraft)
        {
            if (enabled && ParkedAircraft.IsQuiet(___aircraft)) enabled = false;
        }
    }
}
