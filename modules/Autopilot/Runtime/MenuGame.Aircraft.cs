using System;
using System.Collections;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Autopilot.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Vanilla aircraft controls. Every statement is the call the game's own key binding or radial wheel makes
    /// (<c>RadialMenuAction.TriggerAction</c>), with the same availability test (<c>AllowedOnAircraft</c>); owner commands
    /// replicate through the game's own RPCs.
    /// </summary>
    internal static partial class MenuGame
    {
        private const float AirborneRadarAlt = 0.2f;

        private static Aircraft flareOwner;
        private static FlareEjector[] flareEjectors;
        // the game keeps its countermeasure stations (one per display name, alphabetical) in a private list of a private class
        private static readonly FieldInfo CmStations = AccessTools.Field(typeof(CountermeasureManager), "countermeasureStations");
        private static FieldInfo cmStationName;

        private static void ResetAircraftCache() { flareOwner = null; flareEjectors = null; }

        private static Action Bind(MenuRow row) => BindVanilla(row) ?? BindContracts(row);

        // ------------------------------------------------------------------ readers

        private static void ReadAircraft(MenuWorld w)
        {
            if (!TryAircraft(out Aircraft a)) return;
            w.Aircraft = a.definition != null ? MenuText.Upper(a.definition.unitName) : "SELF";
            if (w.Aircraft.Length == 0) w.Aircraft = "SELF";
            w.Airborne = a.radarAlt >= AirborneRadarAlt;
            w.Gear = GearOf(a);
        }

        private static GearPhase GearOf(Aircraft a)
        {
            switch (a.gearState)
            {
                case LandingGear.GearState.LockedExtended: return GearPhase.Down;
                case LandingGear.GearState.LockedRetracted: return GearPhase.Up;
                case LandingGear.GearState.Extending: return GearPhase.Extending;
                case LandingGear.GearState.Retracting: return GearPhase.Retracting;
                default: return GearPhase.None;
            }
        }

        private static void ReadFlight(MenuWorld w)
        {
            if (!TryAircraft(out Aircraft a)) return;
            var filter = a.GetControlsFilter();
            w.HasAssist = filter != null && filter.HasFlightAssist();
            w.Assist = a.flightAssist;
            w.HasHover = filter != null && filter.HasAutoHover();
            w.Hover = w.HasHover && a.IsAutoHoverEnabled();
            w.AutolandEngaged = Engaged();
            w.AutolandBlock = CanLand() ? null : "NOT AVAILABLE NOW";
            w.Ignition = a.Ignition;
            w.CanEject = !a.IsLanded() && !a.HasEjected();
        }

        private static void ReadLights(MenuWorld w)
        {
            if (!TryAircraft(out Aircraft a)) return;
            bool? nav = CockpitStateProbe.NavLightsOn(a);
            w.HasNav = CockpitStateProbe.HasNavLights(a) && nav.HasValue;
            w.NavOn = nav ?? false;
            LandingLight light = LandingLight.Instance;
            w.HasLandingLight = light != null;
            w.LandingLight = light != null ? (int)light.Mode : 0;
            bool? nvg = CockpitStateProbe.NightVisionOn();
            w.HasNvg = NightVision.i != null && nvg.HasValue;
            w.NvgOn = nvg ?? false;
        }

        private static void ReadWeapons(MenuWorld w)
        {
            if (!TryAircraft(out Aircraft a) || a.weaponManager == null || a.weaponStations == null) return;
            WeaponManager wm = a.weaponManager;
            int count = a.weaponStations.Count;
            for (int i = 0; i < count; i++)
            {
                WeaponStation s = a.weaponStations[i];
                var ws = new WorldStation { Name = "STATION " + (i + 1) };
                if (s != null && s.WeaponInfo != null)
                {
                    WeaponInfo info = s.WeaponInfo;
                    ws.Name = MenuText.Upper(!string.IsNullOrEmpty(info.shortName) ? info.shortName : info.weaponName, 14);
                    ws.Ammo = s.Ammo;
                    ws.Class = info.gun ? (s.HasTurret() ? StationClass.Turret : StationClass.Gun)
                        : info.bomb ? StationClass.Bomb
                        : info.missile ? (info.effectiveness.antiSurface > info.effectiveness.antiAir ? StationClass.Agm : StationClass.Missile) : StationClass.Other;
                }
                w.Stations.Add(ws);
                if (s != null && wm.currentWeaponStation == s) w.ActiveStation = i;
            }
            w.HasMultipleGuns = wm.HasMultipleGuns();
            w.GunsLinked = CockpitStateProbe.GunsLinked(wm) ?? false;
            w.HasTurrets = wm.StationsWithTurrets() > 0 && CombatHUD.i != null;
            w.TurretAuto = CombatHUD.i != null && CombatHUD.i.turretAutoControl;
        }

        private static void ReadDefence(MenuWorld w)
        {
            if (!TryAircraft(out Aircraft a)) return;
            CountermeasureManager cm = a.countermeasureManager;
            IList stations = cm != null && CmStations != null ? CmStations.GetValue(cm) as IList : null;
            // activeIndex is 255 after the game auto-chose "none" for a threat: GetActiveCountermeasure would throw
            Countermeasure active = cm != null && (stations == null || cm.activeIndex < stations.Count) ? cm.GetActiveCountermeasure() : null;
            w.HasCountermeasures = cm != null && active != null;
            if (w.HasCountermeasures)
            {
                w.Countermeasure = MenuText.Upper(active.displayName, 20);
                if (stations != null && stations.Count > 1)
                {
                    w.CountermeasureIndex = cm.activeIndex;
                    w.NextCountermeasure = MenuText.Upper(StationName(stations[cm.activeIndex >= stations.Count - 1 ? 0 : cm.activeIndex + 1]), 20); // NextCountermeasure() wraps the same way
                }
                FlareEjector[] ejectors = FlaresOf(a);
                w.HasFlares = ejectors.Length > 0;
                foreach (FlareEjector e in ejectors) if (e != null) { w.Flares += e.GetAmmo(); w.FlaresMax += e.GetMaxAmmo(); }
            }
            w.HasRadar = a.radar != null;
            w.RadarOn = a.radar != null && a.radar.activated;
            IDroneCommand drones = Service<IDroneCommand>();
            w.HasDrones = drones != null;
            if (drones != null) { w.DronesStriking = drones.Striking; w.DronesCanStrike = drones.CanStrike; w.Drones = drones.Ready; }
        }

        private static string StationName(object station)
        {
            if (station == null) return "";
            cmStationName ??= AccessTools.Field(station.GetType(), "displayName");
            return cmStationName?.GetValue(station) as string ?? "";
        }

        /// <summary>The aircraft's flare ejectors, found once per airframe: the game adds the ammo of same-named countermeasures into one station, so the menu does too.</summary>
        private static FlareEjector[] FlaresOf(Aircraft a)
        {
            if (flareOwner != a) { flareOwner = a; flareEjectors = a.GetComponentsInChildren<FlareEjector>(true); }
            return flareEjectors;
        }

        private static void ReadView(MenuWorld w)
        {
            if (!TryAircraft(out Aircraft a)) return;
            CameraStateManager cams = Cameras;
            w.CameraFollowing = cams != null && cams.followingUnit == a;
            w.Camera = cams == null ? ViewKind.Other : InCockpit() ? ViewKind.Cockpit : InOrbit() ? ViewKind.Orbit : ViewKind.Other;
            w.HasCockpitPoint = a.cockpitViewPoint != null;
            w.MapAvailable = !DynamicMap.mapMaximized && SceneSingleton<DynamicMap>.i != null;
            IMissileView missiles = Service<IMissileView>();
            if (missiles == null) return;
            w.MissileViewAvailable = true;
            w.MissileViewActive = missiles.Active;
            w.CanEnterMissileView = missiles.CanEnter;
            w.Missiles = missiles.Count;
            w.MissileStatus = MenuText.Upper(missiles.Status, 30);
            w.MissileCurrent = MenuText.Upper(missiles.CurrentLabel, 24);
        }

        private static void ReadTargets(MenuWorld w)
        {
            CombatHUD hud = CombatHUD.i;
            w.HudReady = hud != null;
            if (hud == null) return;
            var targets = hud.GetTargetList();
            w.Targets = targets?.Count ?? 0;
            if (w.Targets == 0 || !TryAircraft(out Aircraft a)) return;
            float best = float.MaxValue;
            foreach (Unit t in targets)
                if (t != null) best = Mathf.Min(best, (t.transform.position - a.transform.position).magnitude);
            w.NearestKm = best == float.MaxValue ? -1f : best / 1000f;
        }

        // ------------------------------------------------------------------ binding

        private static Action BindVanilla(MenuRow row)
        {
            int index = row.Index;
            switch (row.Id)
            {
                case "weapons/next": return With(a => a.weaponManager.NextWeaponStation());
                case "weapons/prev": return With(a => a.weaponManager.PreviousWeaponStation());
                case "weapons/link": return With(a => a.weaponManager.ToggleGunsLinked());
                case "weapons/turret": return () => CombatHUD.i?.ToggleAutoControl();
                case "lights/nav": return With(a => a.ToggleNavLights());
                case "lights/light": return () => LandingLight.Instance?.Cycle();
                case "lights/nvg": return NightVision.Toggle;
                case "flight/gear": return With(ToggleGear);
                case "flight/assist": return With(a => a.TogglePitchLimiter());
                case "flight/hover": return With(a => a.GetControlsFilter().ToggleAutoHover());
                case "flight/autoland": return ToggleLanding;
                case "flight/engine": return With(a => a.CmdToggleIgnition());
                case "flight/eject": return With(a => a.StartEjectionSequence());
                case "view/cam": return With(a => SwitchCamera(a, cockpit: InOrbit()));
                case "view/map": return OpenMap;
                case "defence/flares": return With(a => a.countermeasureManager.PopFlares());
                case "defence/cm": return With(a => a.countermeasureManager.NextCountermeasure());
                case "defence/radar": return With(a => a.CmdToggleRadar());
                case "targets/keep": return With(KeepNearest);
                case "targets/droplast": return () => CombatHUD.i?.DeselectLast();
                case "targets/clear": return () => CombatHUD.i?.DeselectAll(true);
            }
            if (row.Id.StartsWith("weapons/select/st", StringComparison.Ordinal)) return With(a => SelectStation(a, index));
            return null;
        }

        private static bool Engaged() => AutopilotLandController.Instance?.IsEngaged == true;

        private static bool CanLand()
        {
            AutopilotLandController controller = AutopilotLandController.Instance;
            return controller != null && TryAircraft(out Aircraft aircraft) && controller.CanEngage(aircraft);
        }

        private static void ToggleLanding() => AutopilotLandController.Instance?.Toggle();

        private static bool GearCanMove(Aircraft a) =>
            a.radarAlt >= AirborneRadarAlt &&
            (a.gearState == LandingGear.GearState.LockedExtended || a.gearState == LandingGear.GearState.LockedRetracted);

        private static void ToggleGear(Aircraft a)
        {
            if (!GearCanMove(a)) return;
            a.SetGear(a.gearState == LandingGear.GearState.LockedRetracted);
        }

        private static void SelectStation(Aircraft a, int index)
        {
            if (a.weaponManager == null || a.weaponStations == null || index < 0 || index >= a.weaponStations.Count) return;
            a.SetActiveStation((byte)index);
            CombatHUD.i?.ShowWeaponStation(a.weaponStations[index]);
        }

        /// <summary>Keeps only the selected target nearest the aircraft.</summary>
        private static void KeepNearest(Aircraft a)
        {
            CombatHUD hud = CombatHUD.i;
            var targets = hud != null ? hud.GetTargetList() : null;
            if (targets == null || targets.Count < 2) return;
            Unit nearest = null;
            float best = float.MaxValue;
            foreach (Unit t in targets)
            {
                if (t == null) continue;
                float d = (t.transform.position - a.transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = t; }
            }
            foreach (Unit t in targets.ToArray())
                if (t != null && t != nearest) hud.DeSelectUnit(t);
        }

        private static CameraStateManager Cameras => SceneSingleton<CameraStateManager>.i;

        private static bool InCockpit() => Cameras != null && Cameras.currentState == Cameras.cockpitState;

        private static bool InOrbit() => Cameras != null && Cameras.currentState == Cameras.orbitState;

        private static void SwitchCamera(Aircraft a, bool cockpit)
        {
            CameraStateManager cameras = Cameras;
            if (cameras == null || cameras.followingUnit != a) return;
            if (cockpit && a.cockpitViewPoint != null) cameras.SwitchState(cameras.cockpitState);
            else if (!cockpit) cameras.SwitchState(cameras.orbitState);
        }

        private static void OpenMap()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
        }
    }
}
