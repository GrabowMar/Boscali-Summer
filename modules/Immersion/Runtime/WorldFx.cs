using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
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
        private readonly AirfieldLights airfield = new AirfieldLights();
        private readonly CityLights city = new CityLights();
        private readonly Vapour vapour = new Vapour();
        private readonly MissileBoom boom = new MissileBoom();
        private readonly ArApproach tunnel = new ArApproach();
        private NearMissCrack crack;

        internal NearMissCrack Crack => crack ??= new NearMissCrack(gameObject);

        public void Configure(ImmersionSettings immersionSettings)
        {
            settings = immersionSettings;
            Live = this;
        }

        public string EffectId => "world-fx";

        private void Update()
        {
            bool on = settings != null && settings.Enabled.Value && !Application.isBatchMode;
            // Kelly's KROP plugin draws these when the Compatibility tab hands them over.
            bool own = on && !ForeignMods.WorldFxYielded;
            CrackArmed = own && settings.NearMissCrackEnabled.Value;
            MuffleArmed = on && !ForeignMods.MuffleYielded && settings.CockpitMuffleEnabled.Value;
            ParkedArmed = on && settings.ParkedAircraftEnabled.Value;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            float dt = Time.deltaTime, light = Daylight();
            contrails.Tick(eye, own && settings.ContrailsEnabled.Value, light);
            dust.Tick(eye, own && settings.DustWakeEnabled.Value, light, dt);

            float night = LightPoints.NightLevel();
            LightPoints.Night(night);
            Vector3 global = eye.ToGlobalPosition().AsVector3();
            airfield.Tick(global, on && settings.AirfieldLightsEnabled.Value && !ForeignMods.AirfieldYielded, night, Time.time);
            city.Tick(global, on && settings.CityLightsEnabled.Value, night);
            tunnel.Tick(global, on && settings.ArTunnelEnabled.Value && ImmersionManager.IsCockpitActive, airfield, cam.mainCamera);
            vapour.Tick(eye, on && settings.VapourEnabled.Value, Humidity(), light);
            boom.Tick(eye, on && settings.MissileBoomEnabled.Value && !ForeignMods.MissileBoomYielded, transform);
        }

        /// <summary>0 dry to 1 saturated, from the weather's dewpoint spread at the camera; 0.4 without the Weather module.</summary>
        private static float Humidity()
        {
            if (!ModuleServices.TryGet(out IFlightEnvironmentView view) || !view.TryGet(out FlightEnvironmentSnapshot s) || !s.ValidWeather)
                return .4f;
            // Generous on purpose (fun over realism): a 7 C spread, typical sea air, still reads about one half.
            return Mathf.Max(Mathf.Clamp01(1f - (s.TemperatureC - s.DewpointC) / 15f), s.Condensation01);
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
            airfield.Reset();
            city.Reset();
            vapour.Reset();
            boom.Reset();
            tunnel.Reset();
        }

        public void ReleaseFx()
        {
            ResetForScene();
            crack?.Release();
            crack = null;
            LightPoints.Release();
            tunnel.Release();
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            state["contrailEmitters"] = contrails.Emitters;
            state["dustParticles"] = dust.Particles;
            state["cracks"] = crack != null ? crack.Played : 0;
            state["airfields"] = airfield.Fields;
            state["cityLights"] = city.Lights;
            state["vapourTracked"] = vapour.Tracked;
            state["vapourCone"] = vapour.PeakCone;
            state["vapourTip"] = vapour.PeakTip;
            state["missileBooms"] = boom.Played;
            state["arTunnel"] = tunnel.Visible;
            state["night"] = LightPoints.NightLevel();
            state["humidity"] = Humidity();
            Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (cam != null) state["cameraAltitude"] = cam.transform.position.GlobalY();
            if (GameManager.GetLocalAircraft(out Aircraft own) && own != null)
            {
                float sound = LevelInfo.GetSpeedOfSound(own.transform.position.GlobalY());
                state["ownMach"] = sound > 1f ? own.speed / sound : 0f;
                state["ownAltitude"] = own.transform.position.GlobalY();
                Pilot pilot = own.pilots != null && own.pilots.Length > 0 ? own.pilots[0] : null;
                state["ownG"] = pilot != null ? pilot.gForce + own.transform.up.y : 1f;
            }
        }

        private void OnDestroy()
        {
            ReleaseFx();
            if (Live == this) Live = null;
            CrackArmed = MuffleArmed = ParkedArmed = false;
        }
    }
}
