using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Scenario;
using BoscaliSummer.Modules.Ares.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Ares.Runtime
{
    /// <summary>
    /// The ARES game master: one scene service that owns the map pick, what ARES spawned, the per-unit toggles (protected, frozen,
    /// following) and the cheats. Every mutation is host-only and answers with a line for the console (<see cref="Result"/>). The
    /// MISC tabs (<c>AresTabs</c>) and the sim hook call the same methods.
    /// </summary>
    internal sealed partial class AresHost : MonoBehaviour, ISceneService
    {
        internal const string PickOwner = "boscali.ares";
        internal static AresHost Active { get; private set; }

        private ModConfiguration settings;
        private ManualLogSource log;

        /// <summary>The last action's answer, shown on every ARES page.</summary>
        public string Result { get; private set; } = "Select a unit, then PLACE and click the map.";

        public void Configure(ModConfiguration config, ManualLogSource logger)
        {
            settings = config;
            log = logger;
        }

        private void OnEnable() => Active = this;

        private string Say(string line)
        {
            Result = line ?? "";
            return Result;
        }

        private static bool Host(out string refusal)
        {
            refusal = null;
            // Not MissionManager.IsRunning: it drops to false whenever a mission is set again (seen in a live sim run) while the
            // scene is still fully playable. The spawner is what every action needs.
            if (!GameAccess.IsServer()) refusal = "HOST ONLY: ARES runs on the host.";
            else if (NetworkSceneSingleton<Spawner>.i == null) refusal = "No mission is running.";
            return refusal == null;
        }

        public void ResetForScene()
        {
            StopAllCoroutines(); // a barrage or a delayed seat must not land in the next mission
            Disarm();
            batches.Clear();
            protectedUnits.Clear();
            frozen.Clear();
            followers.Clear();
            catalogs.Clear();
            munitions = null;
            ResetCheats();
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        private void Update()
        {
            TickPick();
            TickCheats();
        }

        private void FixedUpdate() => FixedCheats();

        // ------------------------------------------------------------------ map pick
        //
        // ARES holds the shared MapPicker while armed: Command then swallows unit-icon clicks and box drags, so a click on the map is
        // always ours. A press records where; the release decides the heading when the pointer travelled (press-drag-release).

        private Action<GlobalPosition, float> onPick;
        private bool pickRepeat, pickDown, pauseHeld;
        private int armedFrame;
        private Vector2 downScreen;
        private GlobalPosition downWorld;
        private float lastHeading;
        public string PickLabel { get; private set; }

        public bool Armed => onPick != null;

        private string Arm(string label, string prompt, bool repeat, Action<GlobalPosition, float> pick)
        {
            if (!Host(out string refusal)) return Say(refusal);
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null) return Say("No map.");
            if (!MapPicker.TryArm(PickOwner, MapPicker.GestureLeft, prompt)) return Say("The map is busy with another pick. Finish or cancel it first.");
            if (!DynamicMap.mapMaximized) map.Maximize();
            onPick = pick;
            pickRepeat = repeat;
            pickDown = false;
            armedFrame = Time.frameCount;
            PickLabel = label;
            if (!pauseHeld)
            {
                GameplayUI.AllowPauseKeybind = false; // Esc cancels the pick instead of pausing
                pauseHeld = true;
            }
            return Say(prompt);
        }

        public void Disarm()
        {
            onPick = null;
            pickDown = false;
            PickLabel = null;
            MapPicker.Disarm(PickOwner);
            if (pauseHeld)
            {
                GameplayUI.AllowPauseKeybind = true;
                pauseHeld = false;
            }
        }

        private void TickPick()
        {
            if (onPick == null) return;
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized || !MapPicker.IsOwner(PickOwner))
            {
                Disarm();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Disarm();
                Say("Stopped.");
                return;
            }
            if (Time.frameCount <= armedFrame + 1) return; // the click that pressed the button is not a pick
            if (Input.GetMouseButtonDown(0) && map.TryGetCursorCoordinates(out GlobalPosition at))
            {
                pickDown = true;
                downScreen = Input.mousePosition;
                downWorld = at;
            }
            if (!Input.GetMouseButtonUp(0) || !pickDown) return;
            pickDown = false;
            float heading = lastHeading;
            if (((Vector2)Input.mousePosition - downScreen).sqrMagnitude > 144f && map.TryGetCursorCoordinates(out GlobalPosition up))
                heading = AresMath.HeadingFromDrag(up.x - downWorld.x, up.z - downWorld.z, lastHeading);
            lastHeading = heading;
            Action<GlobalPosition, float> pick = onPick;
            if (!pickRepeat) Disarm();
            try { pick(downWorld, heading); }
            catch (Exception error)
            {
                Say("Failed: " + error.Message);
                log?.LogWarning("[Ares] pick action failed: " + error);
            }
        }

        // ------------------------------------------------------------------ world helpers

        /// <summary>The ground (or sea surface) under a map point, in local space; <paramref name="water"/> when it is sea.</summary>
        internal static Vector3 Ground(GlobalPosition at, out bool water)
        {
            Vector3 local = at.ToLocalPosition();
            var origin = new Vector3(local.x, Mathf.Max(local.y, Datum.LocalSeaY) + 9000f, local.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 18000f, PhysicsLayers.StaticsMask) && hit.point.y > Datum.LocalSeaY + 0.5f)
            {
                water = false;
                return hit.point;
            }
            water = true;
            return new Vector3(local.x, Datum.LocalSeaY, local.z);
        }

        internal static float GroundY(Vector3 local) => Ground(local.ToGlobalPosition(), out _).y;

        /// <summary>The side buttons: BDF, PALA, PMC, PIRATES. Null when this mission has no such side.</summary>
        internal static FactionHQ SideHq(int side)
        {
            switch (side)
            {
                case 0:
                case 1:
                    foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                        if (PortraitFactions.Of(hq) == side) return hq;
                    return null;
                case 2: return FactionRegistry.HqFromName(SideNames.Pmc);
                case 3: return FactionRegistry.HqFromName(SideNames.Pirates);
                default: return null;
            }
        }

        internal static bool LocalAircraft(out Aircraft aircraft) => GameManager.GetLocalAircraft(out aircraft) && aircraft != null;

        private readonly List<Unit> selection = new List<Unit>();

        /// <summary>The units selected on the map now (unit icons only; the local aircraft excluded).</summary>
        public List<Unit> Selection()
        {
            selection.Clear();
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null) return selection;
            foreach (MapIcon icon in map.selectedIcons)
                if (icon is UnitMapIcon unitIcon && unitIcon.unit != null && !GameManager.IsLocalAircraft(unitIcon.unit))
                    selection.Add(unitIcon.unit);
            return selection;
        }

        /// <summary>Silent removal: no kill, no credit (the AI state stops first, or the combat state throws).</summary>
        internal static void Remove(Unit unit)
        {
            if (unit == null || unit.Identity == null) return;
            if (unit is Aircraft aircraft && aircraft.pilots != null)
                foreach (Pilot pilot in aircraft.pilots)
                    if (pilot != null) pilot.SwitchState(null);
            Mirage.ServerObjectManager objects = GameAccess.NetworkManagerOrNull?.ServerObjectManager;
            if (objects != null) objects.Destroy(unit.Identity, !unit.Identity.IsSceneObject);
            else Destroy(unit.gameObject);
        }
    }
}
