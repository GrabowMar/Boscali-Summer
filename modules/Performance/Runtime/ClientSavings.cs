using System.Collections.Generic;
using BepInEx.Bootstrap;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Performance.Configuration;
using BoscaliSummer.Modules.Performance.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Performance.Runtime
{
    /// <summary>
    /// Distance-based client savings, ported from NO_Optimisation by Appulcake
    /// (https://github.com/Appulcake/NO_Optimisation, MIT, Copyright (c) 2026 Appulcake):
    /// the remote turret search skip, far tracer thinning and far unit sleep. Each is a
    /// default-off switch, presentation only, and stands down when that mod is installed
    /// so nothing is patched twice. Not ported: its HUD marker and minimap rewrites (this
    /// mod already owns both surfaces), the livery preflight (needs AssetsTools.NET) and
    /// the scenery detection skip (changes what the host's AI can spot).
    /// </summary>
    internal sealed class ClientSavings : MonoBehaviour, ISceneService
    {
        private const string ForeignGuid = "Apul.NO_Optimisation";
        private const float SweepSeconds = 0.5f;
        private const int MaxChecksPerFrame = 128;

        private static PerformanceSettings settings;
        private static bool foreign;
        private static readonly Dictionary<Unit, Asleep> asleep = new Dictionary<Unit, Asleep>();
        private static int eyeFrame = -1;
        private static Vector3 eye;
        private static uint rng = 0xA341316Cu;
        private int cursor;

        private static bool Client => settings != null && !foreign && !GameManager.IsHeadless;
        internal static bool SkipTurretSearch =>
            Client && settings.SkipRemoteTurretSearch.Value && !GameAccess.IsServer();
        private static bool ThinTracers =>
            Client && settings.ThinDistantTracers.Value && !GameAccess.IsServer();

        internal static bool Sleeping(Unit unit) => asleep.Count > 0 && asleep.ContainsKey(unit);

        public void Configure(PerformanceSettings config)
        {
            settings = config;
            ResetForScene();
        }

        public void ResetForScene()
        {
            WakeAll();
            cursor = 0;
            // Later plugins are not listed yet when this module installs, so ask per scene.
            bool found = Chainloader.PluginInfos.ContainsKey(ForeignGuid);
            if (found && !foreign)
                Plugin.Logger?.LogInfo("Performance: NO_Optimisation is installed; its client patches are used instead of the built-in ones.");
            foreign = found;
        }

        private void OnDisable() => WakeAll();
        private void OnDestroy() => WakeAll();

        private void Update()
        {
            if (!Client || !settings.SleepDistantUnits.Value)
            {
                if (asleep.Count > 0) WakeAll();
                return;
            }
            CameraStateManager cams = SceneSingleton<CameraStateManager>.i;
            List<Unit> units = UnitRegistry.allUnits;
            if (cams == null || cams.mainCamera == null || units == null || units.Count == 0)
            {
                WakeAll();
                return;
            }
            Vector3 from = cams.transform.position;
            // The whole roster is looked at about twice a second, a slice per frame.
            int checks = Mathf.Clamp(Mathf.CeilToInt(units.Count * Time.unscaledDeltaTime / SweepSeconds),
                1, Mathf.Min(MaxChecksPerFrame, units.Count));
            for (int i = 0; i < checks; i++)
            {
                if (cursor >= units.Count) cursor = 0;
                Unit unit = units[cursor++];
                if (unit != null) Check(unit, from, cams);
            }
        }

        private static void Check(Unit unit, Vector3 from, CameraStateManager cams)
        {
            bool sleeping = asleep.ContainsKey(unit);
            bool far = !unit.disabled && (unit is Aircraft || unit is GroundVehicle || unit is Ship) && !Promoted(unit, cams) &&
                ClientTuningMath.ShouldSleep((unit.transform.position - from).sqrMagnitude, sleeping);
            if (far == sleeping) return;
            if (far) asleep[unit] = new Asleep(unit);
            else Wake(unit);
        }

        // Disabled units leave allUnits before the next sweep, so their wrecks must be restored here.
        private static void Wake(Unit unit)
        {
            if (!asleep.TryGetValue(unit, out Asleep state)) return;
            asleep.Remove(unit);
            state.Wake();
        }

        // Whatever the player flies, watches or has targeted always draws.
        private static bool Promoted(Unit unit, CameraStateManager cams)
        {
            if (cams.followingUnit == unit || GameManager.IsLocalAircraft(unit)) return true;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            List<Unit> targets = hud != null ? hud.GetTargetList() : null;
            return targets != null && targets.Contains(unit);
        }

        private static void WakeAll()
        {
            foreach (Asleep state in asleep.Values) state.Wake();
            asleep.Clear();
        }

#if !PUBLIC_RELEASE
        /// <summary>Sandbox-only proof; disables one disposable ground vehicle after exercising sleep/wake ownership.</summary>
        public static Dictionary<string, object> LifecycleProof(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                throw new System.InvalidOperationException("Client savings proof requires a hosted single-player scenario.");
            CameraStateManager cams = SceneSingleton<CameraStateManager>.i;
            if (cams == null) throw new System.InvalidOperationException("A camera is required.");
            ClientSavings service = System.Array.Find(Resources.FindObjectsOfTypeAll<ClientSavings>(),
                candidate => candidate != null && candidate.isActiveAndEnabled);
            if (service == null || settings == null || foreign)
                throw new System.InvalidOperationException("The built-in client savings service is required.");
            uint wanted = (uint)BoscaliSummer.Core.Diagnostics.AutomationArgs.Number(args, "id", 0f);
            string name = BoscaliSummer.Core.Diagnostics.AutomationArgs.Text(args, "name");
            Unit target = null;
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit is GroundVehicle && !unit.disabled && !Promoted(unit, cams) &&
                    (string.IsNullOrEmpty(name) || unit.NetworkUniqueName == name) &&
                    (wanted == 0 || unit.persistentID.Id == wanted)) { target = unit; break; }
            if (target == null) throw new System.InvalidOperationException("A disposable live ground vehicle is required.");
            Wake(target);
            var probe = new GameObject("ClientSavings lifecycle probe", typeof(MeshRenderer), typeof(Light), typeof(ParticleSystem));
            probe.transform.SetParent(target.transform, false);
            Renderer renderer = probe.GetComponent<Renderer>();
            Light light = probe.GetComponent<Light>();
            ParticleSystem particles = probe.GetComponent<ParticleSystem>();
            particles.Play(false);
            Vector3 far = target.transform.position + Vector3.right * 20000f;
            Camera originalCamera = cams.mainCamera;
            var config = settings.SleepDistantUnits.ConfigFile;
            bool save = config.SaveOnConfigSet, enabled = settings.SleepDistantUnits.Value;
            var result = new Dictionary<string, object>();
            try
            {
                config.SaveOnConfigSet = false;
                result["targetName"] = target.NetworkUniqueName;
                Check(target, far, cams);
                result["slept"] = Sleeping(target) && renderer.forceRenderingOff && !light.enabled && particles.isPaused;
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                Wake(target);
                result["normalWake"] = !Sleeping(target) && !renderer.forceRenderingOff && light.enabled;
                result["stoppedParticleStayedStopped"] = !particles.isPlaying;
                Check(target, far, cams);
                settings.SleepDistantUnits.Value = false;
                service.Update();
                result["settingDisableWoke"] = !Sleeping(target) && !renderer.forceRenderingOff && light.enabled;
                settings.SleepDistantUnits.Value = true;
                Check(target, far, cams);
                cams.mainCamera = null;
                service.Update();
                result["missingCameraWoke"] = !Sleeping(target) && !renderer.forceRenderingOff && light.enabled;
                cams.mainCamera = originalCamera;
                Check(target, far, cams);
                service.ResetForScene();
                result["sceneResetWoke"] = !Sleeping(target) && !renderer.forceRenderingOff && light.enabled;
                Check(target, far, cams);
                target.DisableUnit();
                result["disabledRemovedFromRoster"] = target.disabled && !UnitRegistry.allUnits.Contains(target);
                result["disabledWoke"] = !Sleeping(target) && !renderer.forceRenderingOff && light.enabled;
                Check(target, far, cams);
                result["disabledStayedAwake"] = !Sleeping(target);
                return result;
            }
            finally
            {
                cams.mainCamera = originalCamera;
                settings.SleepDistantUnits.Value = enabled;
                config.SaveOnConfigSet = save;
                Wake(target);
                Destroy(probe);
            }
        }
#endif

        internal static bool KeepTracer(Vector3 muzzle, bool tracer)
        {
            if (!ThinTracers) return true;
            if (eyeFrame != Time.frameCount)
            {
                CameraStateManager cams = SceneSingleton<CameraStateManager>.i;
                if (cams == null) return true;
                eyeFrame = Time.frameCount;
                eye = cams.transform.position;
            }
            float keep = ClientTuningMath.TracerKeep((muzzle - eye).sqrMagnitude, tracer);
            if (keep >= 1f) return true;
            if (keep <= 0f) return false;
            // Own generator: the game's random stream is left untouched.
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0x00FFFFFFu) * (1f / 16777216f) < keep;
        }

        /// <summary>What a far unit had switched on, so waking puts back exactly that.</summary>
        private sealed class Asleep
        {
            private readonly Unit owner;
            private readonly Renderer[] renderers;
            private readonly bool[] hidden;
            private readonly ParticleSystem[] particles;
            private readonly bool[] playing;
            private readonly Light[] lights;
            private readonly bool[] lit;

            public Asleep(Unit unit)
            {
                owner = unit;
                renderers = unit.GetComponentsInChildren<Renderer>(true);
                particles = unit.GetComponentsInChildren<ParticleSystem>(true);
                lights = unit.GetComponentsInChildren<Light>(true);
                hidden = new bool[renderers.Length];
                playing = new bool[particles.Length];
                lit = new bool[lights.Length];
                for (int i = 0; i < renderers.Length; i++) hidden[i] = renderers[i].forceRenderingOff;
                for (int i = 0; i < particles.Length; i++) playing[i] = particles[i].isPlaying;
                for (int i = 0; i < lights.Length; i++) lit[i] = lights[i].enabled;
                foreach (Renderer renderer in renderers) renderer.forceRenderingOff = true;
                foreach (ParticleSystem system in particles) if (system.isPlaying) system.Pause(false);
                foreach (Light light in lights) light.enabled = false;
                owner.onDisableUnit += ClientSavings.Wake;
            }

            public void Wake()
            {
                owner.onDisableUnit -= ClientSavings.Wake;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].forceRenderingOff = hidden[i];
                for (int i = 0; i < particles.Length; i++)
                    if (particles[i] != null && playing[i] && particles[i].isPaused) particles[i].Play(false);
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null) lights[i].enabled = lit[i];
            }
        }
    }

    /// <summary>
    /// A remote client runs every turret's datalink search (line-of-sight checks for every
    /// unit on the map) and then does nothing with the answer; only the host acts on it.
    /// </summary>
    [HarmonyPatch(typeof(Turret), "DatalinkTargetSearch")]
    internal static class RemoteTurretSearchPatch
    {
        private static bool Prefix() => !ClientSavings.SkipTurretSearch;
    }

    /// <summary>Turrets of a sleeping unit this peer does not simulate stop animating.</summary>
    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    internal static class SleepingTurretPatch
    {
        private static bool Prefix(Turret __instance)
        {
            Unit unit = __instance.GetAttachedUnit();
            return unit == null || !ClientSavings.Sleeping(unit) || unit.IsServer || unit.LocalSim;
        }
    }

    /// <summary>Other players' visual-only bullets thin out with distance; real bullets never do.</summary>
    [HarmonyPatch(typeof(BulletSim), nameof(BulletSim.AddBullet))]
    internal static class DistantTracerPatch
    {
        private static bool Prefix(bool ___visualOnly, Transform muzzle, bool tracer) =>
            !___visualOnly || muzzle == null || ClientSavings.KeepTracer(muzzle.position, tracer);
    }
}
