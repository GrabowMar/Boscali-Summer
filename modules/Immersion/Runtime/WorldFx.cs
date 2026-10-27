using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Immersion.Audio;
using BoscaliSummer.Modules.Immersion.Configuration;
using BoscaliSummer.Modules.Immersion.Visuals;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    /// <summary>
    /// World-side immersion (2026-10-27): contrails at altitude, a dust wake behind low fast
    /// aircraft over land, the near-miss bullet crack, the cockpit muffle on explosions and quiet parked AI. Client-local
    /// and cosmetic; the patches read the static flags below so they exit at once when a feature is off.
    /// </summary>
    internal sealed class WorldFx : MonoBehaviour, ISceneService, IClientEffect
    {
        internal static WorldFx Live { get; private set; }
        internal static bool CrackArmed { get; private set; }
        internal static bool MuffleArmed { get; private set; }
        internal static bool ParkedArmed { get; private set; }

        private ImmersionSettings settings;
        private readonly Contrails contrails = new Contrails();
        private readonly DustWake dust = new DustWake();
        private NearMissCrack crack;

        internal NearMissCrack Crack => crack ?? (crack = new NearMissCrack(gameObject));
        internal int ContrailEmitters => contrails.Emitters;
        internal int DustParticles => dust.Particles;

        public void Configure(ImmersionSettings immersionSettings)
        {
            settings = immersionSettings;
            Live = this;
        }

        public string EffectId => "world-fx";

        private void Update()
        {
            bool on = settings != null && settings.Enabled.Value && !Application.isBatchMode;
            CrackArmed = on && settings.NearMissCrackEnabled.Value;
            MuffleArmed = on && settings.CockpitMuffleEnabled.Value;
            ParkedArmed = on && settings.ParkedAircraftEnabled.Value;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            float dt = Time.deltaTime, light = Daylight();
            contrails.Tick(eye, on && settings.ContrailsEnabled.Value, light, dt);
            dust.Tick(eye, on && settings.DustWakeEnabled.Value, light, dt);
        }

        /// <summary>0.25 at night to 1 in full sun, so unlit particles do not glow white in the dark.</summary>
        internal static float Daylight()
        {
            Light sun = RenderSettings.sun;
            if (sun == null) return 1f;
            float elevation = Vector3.Dot(-sun.transform.forward, Vector3.up);
            return Mathf.Lerp(.25f, 1f, Mathf.Clamp01(elevation * 5f + .3f));
        }

        public void ResetForScene()
        {
            contrails.Reset();
            dust.Reset();
            crack?.Reset();
        }

        public void ReleaseFx()
        {
            ResetForScene();
            crack?.Release();
            crack = null;
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            state["contrailEmitters"] = contrails.Emitters;
            state["dustParticles"] = dust.Particles;
            state["cracks"] = crack != null ? crack.Played : 0;
        }

        private void OnDestroy()
        {
            ReleaseFx();
            if (Live == this) Live = null;
            CrackArmed = MuffleArmed = ParkedArmed = false;
        }
    }
}
