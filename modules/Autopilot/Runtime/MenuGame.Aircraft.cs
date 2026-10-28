using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
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
            w.HasNvgGain = NightVisionLink.OwnsGain;
            w.NvgGain = NightVisionLink.GainStep;
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
                    CargoStationRead.ReadAmmo(s, out ws.Ammo, out _);
                    ws.Cargo = CargoStationRead.KindOf(s);
                    if (CargoStationRead.UsesLandingZone(s)) w.CargoZoneCapable = true;
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
            BayDoor[] doors = BayTimer != null ? a.GetComponentsInChildren<BayDoor>(true) : Array.Empty<BayDoor>();
            w.HasBayDoors = doors.Length > 0;
            w.BayDoorsOpen = BayHeld(doors);
            ILandingZone zone = Service<ILandingZone>();
            w.CargoZoneAvailable = zone != null;
            if (zone != null && zone.TryGetLocal(out GlobalPosition point))
            {
                w.CargoZoneSet = true;
                GlobalPosition self = a.GlobalPosition();
                float dx = point.x - self.x, dz = point.z - self.z;
                float bearing = (Mathf.Atan2(dx, dz) * Mathf.Rad2Deg + 360f) % 360f;
                w.CargoZoneLeg = bearing.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + " DEG / " +
                    (new Vector2(dx, dz).magnitude / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KM";
            }
            w.CargoFixAvailable = TryCargoGroundFix(out _, out string fixName);
            w.CargoFixName = fixName;
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
                ChaffDispenser dispenser = ChaffDispenser.Instance;
                bool chaffStop = dispenser != null && dispenser.Fitted;     // the dispenser is one more stop of the cycle, after the last station
                if (stations != null && stations.Count + (chaffStop ? 1 : 0) > 1)
                {
                    w.CountermeasureIndex = cm.activeIndex;
                    bool last = cm.activeIndex >= stations.Count - 1;
                    w.NextCountermeasure = chaffStop && !dispenser.Selected && last ? "CHAFF"
                        : MenuText.Upper(StationName(stations[last ? 0 : cm.activeIndex + 1]), 20); // NextCountermeasure() wraps the same way
                }
                FlareEjector[] ejectors = FlaresOf(a);
                w.HasFlares = ejectors.Length > 0;
                foreach (FlareEjector e in ejectors) if (e != null) { w.Flares += e.GetAmmo(); w.FlaresMax += e.GetMaxAmmo(); }
            }
            w.HasRadar = a.radar != null;
            w.RadarOn = a.radar != null && a.radar.activated;
            IDroneCommand drones = Service<IDroneCommand>();
            w.HasDrones = drones != null;
            if (drones != null)
            {
                w.DronesStriking = drones.Striking; w.DronesCanStrike = drones.CanStrike; w.Drones = drones.Ready;
                for (int i = 0; i < drones.Linkable; i++) { w.DroneNames.Add(drones.Name(i)); w.DroneStates.Add(drones.State(i)); }
                w.DroneLinked = drones.LinkedIndex; w.DroneOrdered = drones.Ordered; w.DroneFeed = drones.FeedShown; w.DroneRamMode = drones.RamMode;
            }
            ChaffDispenser chaff = ChaffDispenser.Instance;
            if (chaff != null && chaff.Fitted) { w.HasChaff = true; w.Chaff = chaff.Ammo; w.ChaffMax = chaff.MaxAmmo; }
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
            w.CinematicCamera = Patches.CinematicViewPatch.Setting?.Value ?? true;
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
            if (TryAircraft(out Aircraft own))
            {
                w.Threats = NearestThreat(own, out _, out float metres);
                w.ThreatKm = w.Threats > 0 ? metres / 1000f : -1f;
                MissileWarning warning = own.GetMissileWarningSystem();
                w.Inbound = warning != null && warning.knownMissiles != null ? warning.knownMissiles.Count : 0;
            }
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
                case "flight/lights/nav": return With(a => a.ToggleNavLights());
                case "flight/lights/light": return () => LandingLight.Instance?.Cycle();
                case "view/nvg": return NightVision.Toggle;
                case "view/nvg-gain": return NightVisionLink.CycleGain;
                case "defence/dark": return With(GoDark);
                case "flight/gear": return With(ToggleGear);
                case "flight/assist": return With(a => a.TogglePitchLimiter());
                case "flight/hover": return With(a => a.GetControlsFilter().ToggleAutoHover());
                case "flight/autoland": return ToggleLanding;
                case "flight/engine": return With(a => a.CmdToggleIgnition());
                case "flight/eject": return With(a => a.StartEjectionSequence());
                case "view/cam": return With(a => SwitchCamera(a, cockpit: InOrbit()));
                case "view/map": return OpenMap;
                case "view/cinematic": return () => { var s = Patches.CinematicViewPatch.Setting; if (s != null) s.Value = !s.Value; };
                case "defence/flares": return With(a => { ChaffDispenser.Instance?.Deselect(); a.countermeasureManager.PopFlares(); });
                case "defence/cm": return With(a => a.countermeasureManager.NextCountermeasure());
                case "defence/radar": return With(a => a.CmdToggleRadar());
                case "defence/chaff": return () => ChaffDispenser.Instance?.Pop();
                case "weapons/bay": return With(ToggleBayDoors);
                case "weapons/cargo/next": return With(NextCargo);
                case "weapons/cargo/setzone": return SetCargoZone;
                case "weapons/cargo/clearzone": return () => Service<ILandingZone>()?.ClearLocal();
                case "weapons/cargo/map": return OpenMap;
                case "targets/threat": return With(LockNearestThreat);
                case "targets/inbound": return With(TargetInbound);
                case "targets/keep": return With(KeepNearest);
                case "targets/droplast": return () => CombatHUD.i?.DeselectLast();
                case "targets/clear": return () => CombatHUD.i?.DeselectAll(true);
            }
            if (row.Id.StartsWith("weapons/select/st", StringComparison.Ordinal)) return With(a => SelectStation(a, index));
            if (row.Id.StartsWith("weapons/cargo/st", StringComparison.Ordinal)) return With(a =>
            {
                if (a.weaponStations != null && index >= 0 && index < a.weaponStations.Count && CargoStationRead.KindOf(a.weaponStations[index]) != CargoKind.None)
                    SelectStation(a, index);
            });
            return null;
        }

        /// <summary>Everything that shows the aircraft, off: the radar and the nav lights through the calls their own bindings make, and the menu's landing light.</summary>
        private static void GoDark(Aircraft a)
        {
            if (a.radar != null && a.radar.activated) a.CmdToggleRadar();
            if (CockpitStateProbe.NavLightsOn(a) == true) a.ToggleNavLights();
            LandingLight.Instance?.SwitchOff();
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

        private static void NextCargo(Aircraft a)
        {
            if (a.weaponManager == null || a.weaponStations == null || a.weaponStations.Count == 0) return;
            int current = a.weaponStations.IndexOf(a.weaponManager.currentWeaponStation), count = a.weaponStations.Count;
            for (int step = 1; step <= count; step++)
            {
                int index = (current + step) % count;
                if (CargoStationRead.KindOf(a.weaponStations[index]) == CargoKind.None) continue;
                SelectStation(a, index);
                return;
            }
        }

        /// <summary>A selected airborne contact is not a landing fix. Hostile ground fixes use the faction's known position, never a hidden live transform.</summary>
        private static bool TryCargoGroundFix(out GlobalPosition point, out string name)
        {
            point = default; name = "";
            CombatHUD hud = CombatHUD.i;
            List<Unit> targets = hud != null ? hud.GetTargetList() : null;
            if (targets == null || targets.Count == 0) return false;
            Unit target = targets[0];
            if (target == null || target.disabled || target is Aircraft || target is Missile || target is Ship) return false;
            if (!GameManager.GetLocalHQ(out FactionHQ hq) || hq == null || !hq.TryGetKnownPosition(target, out point)) return false;
            name = target.definition != null ? MenuText.Upper(target.definition.unitName, 25) : "GROUND FIX";
            return true;
        }

        private static void SetCargoZone()
        {
            if (TryCargoGroundFix(out GlobalPosition point, out _)) Service<ILandingZone>()?.SetLocal(point);
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

        /// <summary>Hostile aircraft the player's side tracks: how many, the nearest and its distance in metres (by its known position).</summary>
        private static int NearestThreat(Aircraft a, out Aircraft nearest, out float metres)
        {
            nearest = null;
            metres = float.MaxValue;
            FactionHQ hq = a.NetworkHQ;
            if (hq == null) return 0;
            int count = 0;
            GlobalPosition here = a.GlobalPosition();
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft u = all[i];
                if (u == null || u.disabled || u.NetworkHQ == null || u.NetworkHQ == hq || !hq.TryGetKnownPosition(u, out GlobalPosition known)) continue;
                count++;
                float d = FastMath.Distance(known, here);
                if (d < metres) { metres = d; nearest = u; }
            }
            return count;
        }

        /// <summary>Selects the nearest tracked hostile aircraft alone. One the HUD has no marker for yet cannot be selected: the selection is then left as it is.</summary>
        private static void LockNearestThreat(Aircraft a)
        {
            CombatHUD hud = CombatHUD.i;
            var targets = hud != null ? hud.GetTargetList() : null;
            if (targets == null || NearestThreat(a, out Aircraft threat, out _) == 0) return;
            if (!targets.Contains(threat)) hud.SelectUnit(threat);
            if (!targets.Contains(threat)) return;
            foreach (Unit t in targets.ToArray())
                if (t != threat) hud.DeSelectUnit(t);
        }

        /// <summary>Adds every missile the warning system knows is tracking the aircraft to the selection (guns, turrets, point defence).</summary>
        private static void TargetInbound(Aircraft a)
        {
            CombatHUD hud = CombatHUD.i;
            MissileWarning warning = a.GetMissileWarningSystem();
            var targets = hud != null ? hud.GetTargetList() : null;
            if (targets == null || warning == null || warning.knownMissiles == null) return;
            foreach (Missile m in warning.knownMissiles.ToArray())
                if (m != null && !m.disabled && !targets.Contains(m)) hud.SelectUnit(m);
        }

        // a door closes when its timer runs out; the game opens one for a launch with a few seconds on it
        private static readonly FieldInfo BayTimer = AccessTools.Field(typeof(BayDoor), "openTimer");
        private const float BayHold = 1e9f, BayHeldAbove = 1e6f;

        private static bool BayHeld(BayDoor[] doors)
        {
            foreach (BayDoor door in doors)
                if (door != null && (float)BayTimer.GetValue(door) > BayHeldAbove) return true;
            return false;
        }

        /// <summary>Holds the weapon bay open, or hands it back to the game (which opens it for each launch). Local animation only, like the game's own.</summary>
        private static void ToggleBayDoors(Aircraft a)
        {
            if (BayTimer == null) return;
            BayDoor[] doors = a.GetComponentsInChildren<BayDoor>(true);
            float timer = BayHeld(doors) ? 0f : BayHold;
            foreach (BayDoor door in doors)
                if (door != null) door.OpenDoor(timer);
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
