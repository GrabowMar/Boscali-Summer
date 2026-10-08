using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Vanilla aircraft controls. Every statement is the call the game's own key binding or
    /// radial wheel makes (<c>RadialMenuAction.TriggerAction</c>), with the same availability
    /// test (<c>AllowedOnAircraft</c>); owner commands replicate through the game's own RPCs.
    /// </summary>
    internal static partial class AceRadialCatalog
    {
        private const float AirborneRadarAlt = 0.2f;
        private const int MaxStations = 8;

        // ------------------------------------------------------------------ FLIGHT

        private static AceRadialAction Flight()
        {
            return Branch("flight", "FLIGHT", AceIcon.Flight)
                .Add(Leaf("land", "AUTOLAND", AceIcon.Autopilot, ToggleLanding,
                    visible: () => !Engaged(), enabled: CanLand))
                .Add(Leaf("cancel", "CANCEL AUTOLAND", AceIcon.Autopilot, ToggleLanding,
                    visible: Engaged, status: () => new AceRadialStatus("ENGAGED", AceTone.Active)))
                .Add(Leaf("assist", "FLIGHT ASSIST", AceIcon.Assist, With(a => a.TogglePitchLimiter()),
                    visible: When(a => a.GetControlsFilter() != null && a.GetControlsFilter().HasFlightAssist()),
                    status: Read(a => AceRadialStatus.OnOff(a.flightAssist))))
                .Add(Leaf("hover", "AUTO-HOVER", AceIcon.Hover, With(a => a.GetControlsFilter().ToggleAutoHover()),
                    visible: When(a => a.GetControlsFilter() != null && a.GetControlsFilter().HasAutoHover()),
                    status: Read(a => AceRadialStatus.OnOff(a.IsAutoHoverEnabled()))))
                .Add(Leaf("gear", "LANDING GEAR", AceIcon.Gear, With(ToggleGear),
                    enabled: When(GearCanMove), status: Read(GearStatus)))
                .Add(Leaf("engine", "ENGINE", AceIcon.Engine, With(a => a.CmdToggleIgnition()),
                    status: Read(a => a.Ignition ? new AceRadialStatus("RUNNING", AceTone.Active)
                        : new AceRadialStatus("SHUT DOWN", AceTone.Caution))))
                .Add(Branch("eject", "EJECT", AceIcon.Eject)
                    .Add(Leaf("confirm", "CONFIRM EJECT", AceIcon.Confirm, With(a => a.StartEjectionSequence()),
                        visible: When(a => !a.IsLanded() && !a.HasEjected()),
                        status: () => new AceRadialStatus("NO UNDO", AceTone.Danger))));
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

        private static AceRadialStatus GearStatus(Aircraft a)
        {
            switch (a.gearState)
            {
                case LandingGear.GearState.LockedExtended: return new AceRadialStatus("DOWN", AceTone.Active);
                case LandingGear.GearState.LockedRetracted: return "UP";
                case LandingGear.GearState.Extending: return new AceRadialStatus("EXTENDING", AceTone.Caution);
                case LandingGear.GearState.Retracting: return new AceRadialStatus("RETRACTING", AceTone.Caution);
                default: return AceRadialStatus.None;
            }
        }

        // ------------------------------------------------------------------ LIGHTS

        private static AceRadialAction Lights()
        {
            return Branch("lights", "LIGHTS", AceIcon.Lights)
                .Add(Leaf("nav", "NAV LIGHTS", AceIcon.Lights, With(a => a.ToggleNavLights()),
                    visible: When(CockpitStateProbe.HasNavLights),
                    status: Read(a => OnOff(CockpitStateProbe.NavLightsOn(a)))))
                .Add(Leaf("landing", "LANDING LIGHT", AceIcon.Beam, () => LandingLight.Instance?.Toggle(),
                    visible: () => LandingLight.Instance != null,
                    status: LandingLightStatus))
                .Add(Leaf("auto", "LIGHT AUTO", AceIcon.Gear, () => LandingLight.Instance?.ToggleAuto(),
                    visible: () => LandingLight.Instance != null,
                    status: () => LandingLight.Instance != null && LandingLight.Instance.Auto
                        ? new AceRadialStatus("WITH GEAR", AceTone.Active) : new AceRadialStatus("OFF")))
                .Add(Leaf("nvg", "NIGHT VISION", AceIcon.NightVision, NightVision.Toggle,
                    visible: () => NightVision.i != null,
                    status: () => OnOff(CockpitStateProbe.NightVisionOn())));
        }

        private static AceRadialStatus LandingLightStatus()
        {
            LandingLight light = LandingLight.Instance;
            if (light == null) return AceRadialStatus.None;
            if (light.On) return new AceRadialStatus("ON", AceTone.Active);
            return light.Lit ? new AceRadialStatus("AUTO · LIT", AceTone.Active) : new AceRadialStatus("OFF");
        }

        private static AceRadialStatus OnOff(bool? on) => on.HasValue ? AceRadialStatus.OnOff(on.Value) : AceRadialStatus.None;

        // ------------------------------------------------------------------ WEAPONS

        private static AceRadialAction Weapons()
        {
            return Branch("weapons", "WEAPONS", AceIcon.Weapons)
                .Add(Leaf("next", "NEXT STATION", AceIcon.Next, With(a => a.weaponManager.NextWeaponStation()),
                    visible: When(a => StationCount(a) > 1), status: Read(CurrentStation)))
                .Add(Leaf("prev", "PREV STATION", AceIcon.Previous, With(a => a.weaponManager.PreviousWeaponStation()),
                    visible: When(a => StationCount(a) > 1)))
                .Add(Branch("select", "SELECT", AceIcon.Station).WithChildren(Stations))
                .Add(Leaf("link", "LINK GUNS", AceIcon.Link, With(a => a.weaponManager.ToggleGunsLinked()),
                    visible: When(a => a.weaponManager != null && a.weaponManager.HasMultipleGuns()),
                    status: Read(a => OnOff(CockpitStateProbe.GunsLinked(a.weaponManager)))))
                .Add(Leaf("turret", "TURRET AUTO", AceIcon.Turret, ToggleTurretAuto,
                    visible: When(a => a.weaponManager != null && a.weaponManager.StationsWithTurrets() > 0 && CombatHUD.i != null),
                    status: () => CombatHUD.i != null ? AceRadialStatus.OnOff(CombatHUD.i.turretAutoControl) : AceRadialStatus.None));
        }

        private static int StationCount(Aircraft a) =>
            a.weaponManager != null && a.weaponStations != null ? a.weaponStations.Count : 0;

        private static System.Collections.Generic.IEnumerable<AceRadialAction> Stations()
        {
            if (!TryAircraft(out Aircraft aircraft)) yield break;
            int count = Mathf.Min(StationCount(aircraft), MaxStations);
            for (int i = 0; i < count; i++)
            {
                WeaponStation station = aircraft.weaponStations[i];
                if (station == null || station.WeaponInfo == null) continue;
                int index = i;
                string name = !string.IsNullOrEmpty(station.WeaponInfo.shortName)
                    ? station.WeaponInfo.shortName : station.WeaponInfo.weaponName;
                yield return Leaf("s" + index, Upper(name), station.WeaponInfo.gun ? AceIcon.Turret : AceIcon.Station,
                    With(a => SelectStation(a, index)),
                    status: Read(a => StationStatus(a, index)));
            }
        }

        private static void SelectStation(Aircraft a, int index)
        {
            if (index < 0 || index >= StationCount(a)) return;
            a.SetActiveStation((byte)index);
            CombatHUD.i?.ShowWeaponStation(a.weaponStations[index]);
        }

        private static AceRadialStatus StationStatus(Aircraft a, int index)
        {
            if (index < 0 || index >= StationCount(a)) return AceRadialStatus.None;
            WeaponStation station = a.weaponStations[index];
            bool current = a.weaponManager.currentWeaponStation == station;
            string ammo = "×" + station.Ammo;
            if (station.Ammo <= 0) return new AceRadialStatus("EMPTY", AceTone.Caution);
            return current ? new AceRadialStatus("SELECTED " + ammo, AceTone.Active) : new AceRadialStatus(ammo);
        }

        private static AceRadialStatus CurrentStation(Aircraft a)
        {
            WeaponStation station = a.weaponManager?.currentWeaponStation;
            if (station?.WeaponInfo == null) return AceRadialStatus.None;
            string name = !string.IsNullOrEmpty(station.WeaponInfo.shortName) ? station.WeaponInfo.shortName : station.WeaponInfo.weaponName;
            return Upper(name, 16);
        }

        private static void ToggleTurretAuto() => CombatHUD.i?.ToggleAutoControl();

        // ------------------------------------------------------------------ DEFENCE

        private static AceRadialAction Defence()
        {
            return Branch("defence", "DEFENCE", AceIcon.Defence)
                .Add(Leaf("flares", "POP FLARES", AceIcon.Flare, With(a => a.countermeasureManager.PopFlares()),
                    visible: When(a => a.countermeasureManager != null),
                    enabled: When(a => a.radarAlt >= AirborneRadarAlt && a.countermeasureManager.GetFlareAmmoProportion() > 0f),
                    status: Read(FlareStatus)))
                .Add(Leaf("cm", "NEXT COUNTERMEASURE", AceIcon.Cycle, With(a => a.countermeasureManager.NextCountermeasure()),
                    visible: When(a => a.countermeasureManager != null && a.countermeasureManager.GetActiveCountermeasure() != null),
                    status: Read(a => Upper(a.countermeasureManager.GetActiveCountermeasure()?.displayName, 16))))
                .Add(Leaf("radar", "RADAR", AceIcon.Radar, With(a => a.CmdToggleRadar()),
                    visible: When(a => a.radar != null),
                    status: Read(a => a.radar.activated ? new AceRadialStatus("EMITTING", AceTone.Caution) : new AceRadialStatus("SILENT"))))
                .Add(Leaf("drone-strike", "DRONES: STRIKE", AceIcon.Strike, () => Service<IDroneCommand>()?.Strike(),
                    visible: () => Service<IDroneCommand>() is IDroneCommand d && !d.Striking,
                    enabled: () => Service<IDroneCommand>()?.CanStrike == true,
                    status: () => Service<IDroneCommand>()?.CanStrike == true ? new AceRadialStatus("ON TARGET", AceTone.Active)
                        : new AceRadialStatus("NO TARGET", AceTone.Caution)))
                .Add(Leaf("drone-screen", "DRONES: SCREEN", AceIcon.Defence, () => Service<IDroneCommand>()?.Screen(),
                    visible: () => Service<IDroneCommand>()?.Striking == true,
                    status: () => new AceRadialStatus("STRIKING", AceTone.Caution)));
        }

        private static AceRadialStatus FlareStatus(Aircraft a)
        {
            float left = a.countermeasureManager.GetFlareAmmoProportion();
            string text = Mathf.RoundToInt(left * 100f) + "%";
            return left <= 0f ? new AceRadialStatus("EMPTY", AceTone.Caution)
                : left < 0.25f ? new AceRadialStatus(text, AceTone.Caution) : new AceRadialStatus(text);
        }

        // ------------------------------------------------------------------ VIEW

        private static AceRadialAction View()
        {
            return Branch("view", "VIEW", AceIcon.View)
                .Add(Leaf("cockpit", "COCKPIT VIEW", AceIcon.Cockpit, With(a => SwitchCamera(a, cockpit: true)),
                    visible: When(a => CameraFollowing(a) && !InCockpit() && a.cockpitViewPoint != null)))
                .Add(Leaf("orbit", "EXTERNAL VIEW", AceIcon.Orbit, With(a => SwitchCamera(a, cockpit: false)),
                    visible: When(a => CameraFollowing(a) && !InOrbit())))
                .Add(Leaf("map", "OPEN MAP", AceIcon.Map, OpenMap,
                    visible: () => !DynamicMap.mapMaximized && SceneSingleton<DynamicMap>.i != null))
                .Add(Leaf("mark", "MARK CAMERA POINT", AceIcon.Mark, () => Service<IObservationSource>()?.Capture(),
                    visible: () => Service<IObservationSource>() != null,
                    enabled: () => Service<IObservationSource>()?.CanCapture == true,
                    status: () => Upper(Service<IObservationSource>()?.Status, 18)))
                .Add(Leaf("msl-view", "MISSILE VIEW", AceIcon.Camera, () => Service<IMissileView>()?.Enter(),
                    visible: () => Service<IMissileView>() is IMissileView v && !v.Active && v.CanEnter,
                    status: MissileStatus))
                .Add(Leaf("msl-next", "NEXT MISSILE", AceIcon.Next, () => Service<IMissileView>()?.Next(),
                    visible: () => Service<IMissileView>()?.Active == true,
                    status: MissileCurrent))
                .Add(Leaf("msl-prev", "PREV MISSILE", AceIcon.Previous, () => Service<IMissileView>()?.Prev(),
                    visible: () => Service<IMissileView>()?.Active == true,
                    status: MissileCurrent))
                .Add(Leaf("msl-exit", "EXIT MISSILE VIEW", AceIcon.Clear, () => Service<IMissileView>()?.Exit(),
                    visible: () => Service<IMissileView>()?.Active == true));
        }

        private static AceRadialStatus MissileStatus()
        {
            IMissileView missiles = Service<IMissileView>();
            if (missiles == null || missiles.Count == 0) return AceRadialStatus.None;
            return new AceRadialStatus(Upper(missiles.Status, 18), AceTone.Active);
        }

        private static AceRadialStatus MissileCurrent()
        {
            IMissileView missiles = Service<IMissileView>();
            if (missiles == null || !missiles.Active) return AceRadialStatus.None;
            return new AceRadialStatus(Upper(missiles.CurrentLabel, 18), AceTone.Active);
        }

        // ------------------------------------------------------------------ TARGETS

        /// <summary>The native target list, trimmed with the same calls the target-list keys make (selection is client-local).</summary>
        private static AceRadialAction Targets()
        {
            return Branch("targets", "TARGETS", AceIcon.Target)
                .Add(Leaf("nearest", "KEEP NEAREST", AceIcon.Target, With(KeepNearest),
                    visible: () => CombatHUD.i != null, enabled: () => TargetCount() > 1, status: TargetStatus))
                .Add(Leaf("droplast", "DROP LAST", AceIcon.Previous, () => CombatHUD.i?.DeselectLast(),
                    visible: () => CombatHUD.i != null, enabled: () => TargetCount() > 0))
                .Add(Leaf("clear", "CLEAR TARGETS", AceIcon.Clear, () => CombatHUD.i?.DeselectAll(true),
                    visible: () => CombatHUD.i != null, enabled: () => TargetCount() > 0, status: TargetStatus));
        }

        private static int TargetCount() => CombatHUD.i != null ? CombatHUD.i.GetTargetList()?.Count ?? 0 : 0;

        private static AceRadialStatus TargetStatus()
        {
            int n = TargetCount();
            return n > 0 ? new AceRadialStatus(n + " SELECTED", AceTone.Active) : "NONE";
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

        private static bool CameraFollowing(Aircraft a) => Cameras != null && Cameras.followingUnit == a;

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
