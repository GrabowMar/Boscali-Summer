using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Fx;
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
    /// <c>RadarChaff</c> and a cloud of tumbling metallic fibres. No new network message.
    /// <para>The ejector is deliberately NOT registered with the aircraft's <c>CountermeasureManager</c>: the game addresses countermeasures
    /// by their index in that list, over the network too, and a station only this machine knows would shift or overrun it elsewhere.
    /// It is fired directly instead (the menu's POP CHAFF, the optional key, or the countermeasure button while chaff is selected).
    /// For the pilot it is still one more stop of the game's Next Countermeasure cycle, after the last real station (<see cref="TakeNext"/>):
    /// while it is the selected one the countermeasure button runs the dispenser and the game's own trigger is never raised
    /// (<see cref="TakeTrigger"/>), so the selection stays on this machine and no index it does not know goes to another.
    /// The TAC BOARD's CM tiles find it anyway: they scan the airframe for <c>Countermeasure</c> components.</para>
    /// <para>Fitted only where this machine runs the simulation (single player, hosting): a bundle raises its event locally, and a seeker
    /// that runs on another machine would never hear it.</para>
    /// </summary>
    internal sealed class ChaffDispenser : MonoBehaviour, ISceneService
    {
        private const string DisplayName = "Chaff";
        private const float ServiceSeconds = 0.5f, BundleDrag = 0.02f;

        private static readonly FieldInfo FlarePorts = AccessTools.Field(typeof(FlareEjector), "ejectionPoints");
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
        private static readonly FieldInfo Stations = AccessTools.Field(typeof(CountermeasureManager), "countermeasureStations");

        /// <summary>Every private field the kit writes still exists; a game update that renames one leaves the aircraft without chaff, not broken.</summary>
        private static readonly bool Supported =
            FlarePorts != null && FlareVelocity != null && FlareVariance != null && FlareSound != null && FlareVolume != null &&
            ChaffPorts != null && ChaffPrefab != null && ChaffVelocity != null && ChaffVariance != null && ChaffSound != null && ChaffVolume != null &&
            ChaffDoors != null && ChaffInterval != null && ChaffGrouping != null && ChaffMax != null &&
            BundleTime != null && BundleDragField != null && BundleParticles != null;

        private AutopilotSettings settings;
        private Aircraft owner;
        private ChaffEjector ejector;
        private FlareEjector[] flares = Array.Empty<FlareEjector>();
        private GameObject bundleHolder, bundle;
        private readonly ChaffVisuals visuals = new ChaffVisuals();
        private int load, flaresBefore;
        private bool rearmPending;
        private float salvoUntil, nextFire, nextService;
        private Sprite flareIcon;
        private int heldFrame = -10;

        public static ChaffDispenser Instance { get; private set; }

        /// <summary>The local aircraft carries the dispenser.</summary>
        public bool Fitted => ejector != null;
        public int Ammo => ejector != null ? ejector.ammo : 0;
        public int MaxAmmo => load;

        /// <summary>Radar missiles a bundle has broken the lock of since the game started (the chaff scenario reads it).</summary>
        public static int Seduced { get; private set; }

        /// <summary>Chaff is the pilot's selected countermeasure: the countermeasure button dispenses it.</summary>
        public bool Selected { get; private set; }

        /// <summary>
        /// The game's Next Countermeasure step on the local aircraft. From the last real station it lands on chaff (true: the game's own step
        /// is skipped); from chaff it hands back to the game, whose step wraps to its first station.
        /// </summary>
        public bool TakeNext(CountermeasureManager manager)
        {
            if (ejector == null || owner == null || manager != owner.countermeasureManager) return false;
            if (Selected) { Selected = false; return false; }
            if (!(Stations?.GetValue(manager) is ICollection stations) || !ChaffRules.NextIsChaff(manager.activeIndex, stations.Count)) return false;
            if (owner.countermeasureTrigger) owner.Countermeasures(false, manager.activeIndex);     // what the game's own step does: let go of the old one
            Selected = true;
            ShowSelected();
            return true;
        }

        /// <summary>
        /// The countermeasure button on the local aircraft while chaff is selected: the press is noted for this frame and kept from the game
        /// (true), so no flare leaves and nothing is sent. The game asks again every frame the button is held, because its trigger never rises.
        /// </summary>
        public bool TakeTrigger(Aircraft aircraft, bool active)
        {
            if (!Selected || !active || aircraft != owner) return false;
            heldFrame = Time.frameCount;
            return true;
        }

        /// <summary>The dispenser, when it is this manager's selected countermeasure; else null.</summary>
        public Countermeasure SelectedFor(CountermeasureManager manager) =>
            Selected && owner != null && manager == owner.countermeasureManager ? ejector : null;

        /// <summary>Hand the selection back to the game's own station (POP FLARES selects the flares, as in the game).</summary>
        public void Deselect() => Selected = false;

        /// <summary>The game's countermeasure readout shows the dispenser (the flare's picture: the game has no chaff sprite, and none is a white block).</summary>
        private void ShowSelected()
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || ejector == null || hud.aircraft != owner) return;
            hud.DisplayCountermeasures(DisplayName, flareIcon, ejector.ammo);
            hud.DisplayCountermeasureAmmo(ejector.ammo);
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
            visuals.Clear();
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

            bool chosen = Selected && Time.frameCount - heldFrame <= 1;     // the button, asked for by the game this frame or the last (update order)
            if (!ChaffRules.ShouldRun(now < salvoUntil, chosen, ejector.ammo) || now < nextFire) return;
            nextFire = now + ChaffRules.Interval;
            ejector.Fire();
            Seduce(aircraft);
            aircraft.RequestRearm();      // what the game does for every flare it fires: the rearm that follows refills the dispenser too
            // Fire() wrote the chaff count into the native countermeasure readout; put the pilot's selected one back
            CountermeasureManager manager = aircraft.countermeasureManager;
            if (Selected) ShowSelected();
            else if (manager != null && manager.activeIndex != byte.MaxValue) manager.UpdateHUD();
        }

        private bool KeyPressed()
        {
            KeyCode key = settings.ChaffKey.Value;
            return key != KeyCode.None && Input.GetKeyDown(key) && !GameplayUI.GameIsPaused && !InputFieldChecker.InsideInputField;
        }

        /// <summary>
        /// One ejection's roll against every radar missile tracking the aircraft (<see cref="ChaffRules.SeduceChance"/>). A seduced missile
        /// loses its target the way the game's own lost lock does: the target cleared, so an ARH flies on straight and self-destructs (its slow
        /// check), and an armed SARH detonates at once (its Seek with no target). Runs where the missiles are simulated (the dispenser is fitted only there).
        /// </summary>
        private static void Seduce(Aircraft aircraft)
        {
            if (MissileSeekerField == null || SeekerTarget == null) return;
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            List<Missile> known = warning != null ? warning.knownMissiles : null;
            Vector3 track = aircraft.rb != null ? aircraft.rb.velocity.normalized : aircraft.transform.forward;
            for (int i = 0; known != null && i < known.Count; i++)
            {
                Missile m = known[i];
                if (m == null || m.disabled || m.targetID != aircraft.persistentID || !ChaffRules.IsRadarSeeker(m.GetSeekerType())) continue;
                Vector3 los = aircraft.transform.position - m.transform.position;
                float beam = 1f - Mathf.Abs(Vector3.Dot(los.normalized, track));
                if (UnityEngine.Random.value >= ChaffRules.SeduceChance(los.magnitude, beam)) continue;
                try
                {
                    var seeker = MissileSeekerField.GetValue(m) as MissileSeeker;
                    if (seeker is SARHSeeker) SarhTransform?.SetValue(seeker, null);   // SARH: no track to come back to
                    SeekerTarget.SetValue(seeker, null);
                    m.SetTarget(null);
                    m.SetProxyFuse(null, null);
                    Seduced++;
                    Plugin.Logger?.LogInfo("Chaff: " + m.GetSeekerType() + " missile seduced at " + Mathf.RoundToInt(los.magnitude) + " m, beam " + beam.ToString("F2"));
                }
                catch (Exception e) { Plugin.Logger?.LogDebug("Chaff: seduction failed (" + e.Message + ")"); }
            }
        }

        private static readonly FieldInfo MissileSeekerField = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo SeekerTarget = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo SarhTransform = AccessTools.Field(typeof(SARHSeeker), "targetTransform");

        /// <summary>Refill with the rest of the aircraft (the dispenser is not in the game's own rearm list).</summary>
        private void Service(Aircraft aircraft)
        {
            int flaresNow = 0;
            foreach (FlareEjector flare in flares) if (flare != null) flaresNow += flare.GetAmmo();
            bool pending = aircraft.HasRequestedRearm;
            if (ejector.ammo < load && ChaffRules.Refilled(rearmPending, pending, flaresBefore, flaresNow)) ejector.ammo = load;
            rearmPending = pending;
            flaresBefore = flaresNow;
            if (Selected) ShowSelected();      // a rearm or a refit lets the game write its own station back into the readout
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
                flareIcon = flare.displayImage;
                var ports = FlarePorts.GetValue(flare) as FlareEjector.EjectionPoint[];
                GameObject round = Bundle();
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
                fitted.displayImage = flareIcon;      // Fire() writes it into the cockpit readout; null drew a white block there
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
        /// The bundle the ejector spawns: the game's <c>RadarChaff</c> (flight, lifetime, the seeker event) with a foil burst.
        /// Kept active under an inactive holder, so every copy the game instantiates wakes up and the template never runs.
        /// </summary>
        private GameObject Bundle()
        {
            if (bundle != null) return bundle;
            if (Datum.origin == null) return null;

            bundleHolder = new GameObject("Boscali / Chaff Bundle Holder");
            bundleHolder.SetActive(false);
            bundleHolder.transform.SetParent(transform, false);
            var go = new GameObject("Boscali Chaff Bundle");
            go.transform.SetParent(bundleHolder.transform, false);

            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            // Missing render assets must never disable the native countermeasure.
            visuals.Configure(particles, Datum.origin, ChaffRules.BundleSeconds);

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
            Selected = false;
            flareIcon = null;
            flares = Array.Empty<FlareEjector>();
            load = 0;
            flaresBefore = 0;
            rearmPending = false;
            salvoUntil = nextFire = nextService = 0f;
        }
    }
}
