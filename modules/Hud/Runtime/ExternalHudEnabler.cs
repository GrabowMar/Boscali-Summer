using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Hud.Runtime
{
    /// <summary>
    /// Forces the vanilla flight HUD -- and, outside a maximized map, the minimap -- visible
    /// while the local pilot's own live aircraft is viewed externally (orbit or chase), so the
    /// native instruments and this module's status panel (itself native's own child) are not
    /// left blank. Vanilla disables <c>FlightHud</c>'s canvas on entry to those camera states
    /// (<c>CameraOrbitState</c>/<c>CameraChaseState.EnterState</c>), so the condition is
    /// re-checked every tick rather than once on a state transition. No Harmony patch: this only
    /// ever flips a GameObject the module did not create back to a state vanilla itself is
    /// capable of, through <c>FlightHud.EnableCanvas</c>/<c>DynamicMap.EnableCanvas</c>, and the
    /// prior <c>activeSelf</c> is restored exactly once the condition ends for any reason.
    /// </summary>
    internal sealed class ExternalHudEnabler
    {
        private static readonly FieldInfo CanvasField = AccessTools.Field(typeof(FlightHud), "canvas");

        private bool hudForced, mapForced;
        private bool hudPriorActive, mapPriorActive;

        /// <summary>
        /// True while the local player's own live aircraft is being viewed in the orbit or chase
        /// camera state: not disabled, not ejected, cockpit attached, the camera's followed unit
        /// is that same aircraft. A pure query with no side effects, shared by the forcing gate
        /// below and by the status panel's target-camera inset gate (never shown in cockpit).
        /// </summary>
        public static bool IsOwnAircraftExternalView(out Aircraft aircraft)
        {
            aircraft = null;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Aircraft candidate = hud != null ? hud.aircraft : null;
            if (cam == null || candidate == null || candidate.disabled || candidate.HasEjected() ||
                candidate.Player == null || !candidate.Player.IsLocalPlayer ||
                cam.followingUnit != candidate || candidate.cockpit == null || candidate.cockpit.IsDetached() ||
                (cam.currentState != cam.orbitState && cam.currentState != cam.chaseState))
                return false;
            aircraft = candidate;
            return true;
        }

        /// <summary>
        /// Re-evaluate this tick. <paramref name="settingEnabled"/> is the pilot's
        /// <c>Hud.ExternalHud</c> preference ANDed with the board being enabled;
        /// <paramref name="viewingOwnExternally"/> is <see cref="IsOwnAircraftExternalView"/>,
        /// computed once per tick by the caller so the status panel's camera inset can share the
        /// same read. The map and pause/cinematic/batch gates are ours: vanilla itself decides
        /// nothing about them for this purpose.
        /// </summary>
        public void Tick(bool settingEnabled, bool viewingOwnExternally)
        {
            if (!settingEnabled || !viewingOwnExternally || Application.isBatchMode ||
                DynamicMap.mapMaximized || GameplayUI.GameIsPaused || PlayerSettings.cinematicMode)
            {
                Release();
                return;
            }

            FlightHud flightHud = SceneSingleton<FlightHud>.i;
            Canvas canvas = flightHud != null ? CanvasField?.GetValue(flightHud) as Canvas : null;
            if (canvas != null)
            {
                GameObject hudObject = canvas.gameObject;
                if (!hudForced) { hudPriorActive = hudObject.activeSelf; hudForced = true; }
                if (!hudObject.activeSelf) FlightHud.EnableCanvas(true);
            }

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null)
            {
                GameObject mapObject = map.gameObject;
                if (!mapForced) { mapPriorActive = mapObject.activeSelf; mapForced = true; }
                if (!mapObject.activeSelf) DynamicMap.EnableCanvas(true);
            }
        }

        /// <summary>
        /// Restore exactly what this enabler forced, then forget it. Idempotent, and safe to call
        /// every tick the condition is false as well as on scene reset and feature teardown.
        /// Never deactivates a GameObject this enabler did not activate, and never restores the
        /// minimap while it is maximized -- vanilla owns it then.
        /// </summary>
        public void Release()
        {
            if (hudForced)
            {
                FlightHud flightHud = SceneSingleton<FlightHud>.i;
                Canvas canvas = flightHud != null ? CanvasField?.GetValue(flightHud) as Canvas : null;
                if (canvas != null) canvas.gameObject.SetActive(hudPriorActive);
                hudForced = false;
            }
            if (mapForced)
            {
                if (!DynamicMap.mapMaximized)
                {
                    DynamicMap map = SceneSingleton<DynamicMap>.i;
                    if (map != null) map.gameObject.SetActive(mapPriorActive);
                }
                mapForced = false;
            }
        }
    }
}
