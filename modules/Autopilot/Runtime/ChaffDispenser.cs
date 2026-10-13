using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Autopilot.Domain;
using HarmonyLib;
using NuclearOption.MissionEditorScripts;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// A chaff dispenser on the local player's own aircraft. The game ships the whole mechanic (<c>ChaffEjector</c>, <c>RadarChaff</c>,
    /// and both radar seekers listening on <c>Unit.onAddRadarChaff</c>) but no aircraft carries an ejector and no bundle prefab exists,
    /// so this builds both from the game's own classes: one <c>ChaffEjector</c> on the flare ejector's ports, firing a bundle made of a
    /// <c>RadarChaff</c> and a puff in the flare smoke's material. No patch, no network message.
    /// <para>The ejector is deliberately NOT registered with the aircraft's <c>CountermeasureManager</c>: the game addresses countermeasures
    /// by their index in that list, over the network too, and a station only this machine knows would shift or overrun it elsewhere.
    /// It is fired directly instead (the menu's POP CHAFF, the optional key, or the countermeasure button while a radar missile is inbound).
    /// The TAC BOARD's CM tiles find it anyway: they scan the airframe for <c>Countermeasure</c> components.</para>
    /// <para>Fitted only where this machine runs the simulation (single player, hosting): a bundle raises its event locally, and a seeker
    /// that runs on another machine would never hear it.</para>
    /// </summary>
    internal sealed class ChaffDispenser : MonoBehaviour, ISceneService
    {
        private const string DisplayName = "Chaff";
        private const float ServiceSeconds = 0.5f, BundleDrag = 0.02f;

        private static readonly FieldInfo FlarePorts = AccessTools.Field(typeof(FlareEjector), "ejectionPoints");
        private static readonly FieldInfo FlarePrefab = AccessTools.Field(typeof(FlareEjector), "flarePrefab");
        private static readonly FieldInfo FlareVelocity = AccessTools.Field(typeof(FlareEjector), "ejectionVelocity");
        private static readonly FieldInfo FlareVariance = AccessTools.Field(typeof(FlareEjector), "ejectionVelocityVariance");
        private static readonly FieldInfo FlareSound = AccessTools.Field(typeof(FlareEjector), "ejectionSound");
        private static readonly FieldInfo FlareVolume = AccessTools.Field(typeof(FlareEjector), "ejectionVolume");
        private static readonly FieldInfo ChaffPorts = AccessTools.Field(typeof(ChaffEjector), "ejectionPoints");
        private static readonly FieldInfo ChaffPrefab = AccessTools.Field(typeof(ChaffEjector), "chaffPrefab");
        private static readonly FieldInfo ChaffVelocity = AccessTools.Field(typeof(ChaffEjector), "ejectionVelocity");
        private static readonly FieldInfo ChaffVariance = AccessTools.Field(typeof(ChaffEjector), "ejectionVelocityVariance");
        private static readonly FieldInfo ChaffSound = AccessTools.Field(typeof(ChaffEjector), "ejectionSound");
        private static readonly FieldInfo ChaffVolume = AccessTools.Field(typeof(ChaffEjector), "ejectionVolume");
        private static readonly FieldInfo ChaffDoors = AccessTools.Field(typeof(ChaffEjector), "flareDoors");
        private static readonly FieldInfo ChaffInterval = AccessTools.Field(typeof(ChaffEjector), "ejectionInterval");
        private static readonly FieldInfo ChaffGrouping = AccessTools.Field(typeof(ChaffEjector), "ejectionGrouping");
        private static readonly FieldInfo ChaffMax = AccessTools.Field(typeof(ChaffEjector), "maxAmmo");
        private static readonly FieldInfo BundleTime = AccessTools.Field(typeof(RadarChaff), "chaffTime");
        private static readonly FieldInfo BundleDragField = AccessTools.Field(typeof(RadarChaff), "drag");
        private static readonly FieldInfo BundleParticles = AccessTools.Field(typeof(RadarChaff), "chaffParticles");

        /// <summary>Every private field the kit writes still exists; a game update that renames one leaves the aircraft without chaff, not broken.</summary>
        private static readonly bool Supported =
            FlarePorts != null && FlarePrefab != null && FlareVelocity != null && FlareVariance != null && FlareSound != null && FlareVolume != null &&
            ChaffPorts != null && ChaffPrefab != null && ChaffVelocity != null && ChaffVariance != null && ChaffSound != null && ChaffVolume != null &&
            ChaffDoors != null && ChaffInterval != null && ChaffGrouping != null && ChaffMax != null &&
            BundleTime != null && BundleDragField != null && BundleParticles != null;

        private AutopilotSettings settings;
        private Aircraft owner;
        private ChaffEjector ejector;
        private FlareEjector[] flares = Array.Empty<FlareEjector>();
        private GameObject bundleHolder, bundle;
        private int load, flaresBefore;
        private bool rearmPending;
        private float salvoUntil, nextFire, nextService;

        public static ChaffDispenser Instance { get; private set; }

        /// <summary>The local aircraft carries the dispenser.</summary>
        public bool Fitted => ejector != null;
        public int Ammo => ejector != null ? ejector.ammo : 0;
        public int MaxAmmo => load;

        /// <summary>The countermeasure button also runs the dispenser while a radar missile is inbound.</summary>
        public bool Linked
        {
            get => settings != null && settings.ChaffOnCountermeasureKey.Value;
            set { if (settings != null) settings.ChaffOnCountermeasureKey.Value = value; }
        }

        public void Configure(AutopilotSettings value)
        {
            settings = value;
            if (!Supported) Plugin.Logger?.LogWarning("Autopilot: the game's chaff classes changed; the chaff dispenser is off.");
        }

        /// <summary>One salvo: the dispenser runs for <see cref="ChaffRules.SalvoSeconds"/>.</summary>
        public void Pop()
        {
            if (ejector != null) salvoUntil = Time.timeSinceLevelLoad + ChaffRules.SalvoSeconds;
        }

        public void ResetForScene()
        {
            Drop();
            if (bundleHolder != null) Destroy(bundleHolder);     // its particles live in the old scene's origin space
            bundleHolder = null;
            bundle = null;
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            ResetForScene();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!Supported || settings == null) return;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || aircraft.disabled) return;
            if (aircraft != owner) Fit(aircraft);
            if (ejector == null) return;

            float now = Time.timeSinceLevelLoad;
            if (now >= nextService) { nextService = now + ServiceSeconds; Service(aircraft); }
            if (KeyPressed()) Pop();

            bool held = aircraft.countermeasureTrigger;
            if (!ChaffRules.ShouldRun(now < salvoUntil, Linked, held, held && RadarMissileInbound(aircraft), ejector.ammo) || now < nextFire) return;
            nextFire = now + ChaffRules.Interval;
            ejector.Fire();
            aircraft.RequestRearm();      // what the game does for every flare it fires: the rearm that follows refills the dispenser too
            // Fire() wrote the chaff count into the native countermeasure readout; put the pilot's selected one back
            CountermeasureManager manager = aircraft.countermeasureManager;
            if (manager != null && manager.activeIndex != byte.MaxValue) manager.UpdateHUD();
        }

        private bool KeyPressed()
        {
            KeyCode key = settings.ChaffKey.Value;
            return key != KeyCode.None && Input.GetKeyDown(key) && !GameplayUI.GameIsPaused && !InputFieldChecker.InsideInputField;
        }

        private static bool RadarMissileInbound(Aircraft aircraft)
        {
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            List<Missile> known = warning != null ? warning.knownMissiles : null;
            for (int i = 0; known != null && i < known.Count; i++)
                if (known[i] != null && !known[i].disabled && ChaffRules.IsRadarSeeker(known[i].GetSeekerType())) return true;
            return false;
        }

        /// <summary>Refill with the rest of the aircraft (the dispenser is not in the game's own rearm list).</summary>
        private void Service(Aircraft aircraft)
        {
            int flaresNow = 0;
            foreach (FlareEjector flare in flares) if (flare != null) flaresNow += flare.GetAmmo();
            bool pending = aircraft.HasRequestedRearm;
            if (ejector.ammo < load && ChaffRules.Refilled(rearmPending, pending, flaresBefore, flaresNow)) ejector.ammo = load;
            rearmPending = pending;
            flaresBefore = flaresNow;
        }

        private void Fit(Aircraft aircraft)
        {
            Drop();
            owner = aircraft;
            if (!aircraft.IsServer) return;
            try
            {
                flares = aircraft.GetComponentsInChildren<FlareEjector>(true);
                if (flares.Length == 0) return;
                FlareEjector flare = flares[0];
                var ports = FlarePorts.GetValue(flare) as FlareEjector.EjectionPoint[];
                GameObject round = Bundle(flare);
                if (ports == null || ports.Length == 0 || round == null) return;

                var chaffPorts = new ChaffEjector.EjectionPoint[ports.Length];
                for (int i = 0; i < ports.Length; i++) chaffPorts[i] = new ChaffEjector.EjectionPoint { part = ports[i].part, transform = ports[i].transform };
                int flaresMax = 0;
                foreach (FlareEjector f in flares) if (f != null) { flaresMax += f.GetMaxAmmo(); flaresBefore += f.GetAmmo(); }
                load = ChaffRules.Load(flaresMax);

                var go = new GameObject("Boscali / Chaff");
                go.transform.SetParent(aircraft.transform, false);
                // Awake runs here with no aircraft set, so the game's station list never hears of this ejector
                ChaffEjector fitted = go.AddComponent<ChaffEjector>();
                fitted.displayName = DisplayName;
                fitted.ammo = load;
                ChaffMax.SetValue(fitted, load);
                ChaffPorts.SetValue(fitted, chaffPorts);
                ChaffPrefab.SetValue(fitted, round);
                ChaffDoors.SetValue(fitted, Array.CreateInstance(ChaffDoors.FieldType.GetElementType(), 0));
                ChaffVelocity.SetValue(fitted, FlareVelocity.GetValue(flare));
                ChaffVariance.SetValue(fitted, FlareVariance.GetValue(flare));
                ChaffSound.SetValue(fitted, FlareSound.GetValue(flare));
                ChaffVolume.SetValue(fitted, FlareVolume.GetValue(flare));
                ChaffInterval.SetValue(fitted, ChaffRules.Interval * 0.5f);     // our own clock paces it; the ejector's must never be the slower one
                ChaffGrouping.SetValue(fitted, ChaffRules.Grouping);
                fitted.aircraft = aircraft;
                ejector = fitted;
                rearmPending = aircraft.HasRequestedRearm;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Autopilot: chaff dispenser not fitted (" + e.Message + ")");
                Drop();
                owner = aircraft;       // do not try again every frame on this airframe
            }
        }

        /// <summary>
        /// The bundle the ejector spawns: the game's <c>RadarChaff</c> (flight, lifetime, the seeker event) with a glitter puff in the flare
        /// smoke's material. Kept active under an inactive holder, so every copy the game instantiates wakes up and the template never runs.
        /// </summary>
        private GameObject Bundle(FlareEjector flare)
        {
            if (bundle != null) return bundle;
            Material puff = null;
            var flareRound = FlarePrefab.GetValue(flare) as GameObject;
            if (flareRound != null)
                foreach (ParticleSystemRenderer r in flareRound.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    if (r.sharedMaterial == null) continue;
                    if (puff == null) puff = r.sharedMaterial;
                    if (r.sharedMaterial.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0) { puff = r.sharedMaterial; break; }
                }
            if (puff == null || Datum.origin == null) return null;

            bundleHolder = new GameObject("Boscali / Chaff Bundle Holder");
            bundleHolder.SetActive(false);
            bundleHolder.transform.SetParent(transform, false);
            var go = new GameObject("Boscali Chaff Bundle");
            go.transform.SetParent(bundleHolder.transform, false);

            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;                                   // RadarChaff destroys the bundle only while its particles still play
            main.duration = ChaffRules.BundleSeconds * 2f;      // so the opening burst happens once
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startColor = new Color(0.86f, 0.9f, 0.96f, 0.5f);
            main.gravityModifier = 0.02f;
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;    // the flare smoke's choice: the cloud stays put through origin shifts
            main.customSimulationSpace = Datum.origin;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 5f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f;
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = puff;

            RadarChaff chaff = go.AddComponent<RadarChaff>();
            BundleTime.SetValue(chaff, ChaffRules.BundleSeconds);
            BundleDragField.SetValue(chaff, BundleDrag);
            BundleParticles.SetValue(chaff, particles);
            bundle = go;
            return bundle;
        }

        private void Drop()
        {
            if (ejector != null) Destroy(ejector.gameObject);
            ejector = null;
            owner = null;
            flares = Array.Empty<FlareEjector>();
            load = 0;
            flaresBefore = 0;
            rearmPending = false;
            salvoUntil = nextFire = nextService = 0f;
        }
    }
}
