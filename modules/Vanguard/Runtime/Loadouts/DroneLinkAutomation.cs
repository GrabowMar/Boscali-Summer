#if !PUBLIC_RELEASE
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    public static partial class VanguardAutomation
    {
        private static Dictionary<string, object> LinkStep(Dictionary<string, object> args, Aircraft lead)
        {
            if (!ModuleServices.TryGet(out IDroneCommand command) || !(command is DroneLinkService link))
                return Failure("VanguardAutomation", "link", "no drone link service");
            int index = (int)Number(args, "index", Mathf.Max(0, link.LinkedIndex));
            DronePilot pilot = DronePilot.Active;
            DroneFlightHud hud = pilot != null ? pilot.GetComponent<DroneFlightHud>() : null;
            switch (Text(args, "do") ?? "status")
            {
                case "link": link.Link(index); break;
                case "pilot": link.Pilot(index); break;
                case "release": link.StopPiloting(); break;
                case "hud-pointer":
                    if (hud == null || !hud.Visible) return Failure("VanguardAutomation", "link", "drone HUD unavailable");
                    DronePilotInput.TestPointer = true;
                    break;
                case "hud-click":
                    if (hud == null || !hud.ClickReturn()) return Failure("VanguardAutomation", "link", "return button did not receive the click");
                    break;
                case "return":
                    if (pilot == null || !pilot.RequestReturn()) return Failure("VanguardAutomation", "link", "return was blocked");
                    break;
                case "map-focus":
                    CameraStateManager stock = SceneSingleton<CameraStateManager>.i;
                    Missile followed = link.Linked?.Missile;
                    if (!DynamicMap.mapMaximized || stock == null || followed == null)
                        return Failure("VanguardAutomation", "link", "no target to focus from map");
                    stock.SetFollowingUnit(followed);
                    break;
                case "share": link.ToggleShare(index); break;
                case "feed": link.ToggleFeed(); break;
                case "look": link.ToggleLook(); break;
                case "zoom": link.CycleZoom(); break;
                case "view":
                case "weapon":
                    if (pilot == null || pilot.Current == null) return Failure("VanguardAutomation", "link", "no active pilot");
                    if (Text(args, "do") == "view") pilot.SwitchView(); else pilot.CycleWeapon();
                    break;
                case "input":
                    if (pilot == null || pilot.Current == null) return Failure("VanguardAutomation", "link", "no active pilot");
                    pilot.SetTestInput(new Vector3(Number(args, "pitch", 0f), Number(args, "yaw", 0f), Number(args, "roll", 0f)),
                        Number(args, "throttle", 0.7f), Number(args, "fire", 0f) > 0f);
                    break;
                case "mfd":
                    DynamicMap map = SceneSingleton<DynamicMap>.i;
                    if (map != null && !DynamicMap.mapMaximized) map.Maximize();
                    AvConsole.Open(MfdSlots.Drones, "DRONES");
                    break;
                case "unmap":
                    DynamicMap current = SceneSingleton<DynamicMap>.i;
                    if (current != null && DynamicMap.mapMaximized) current.Minimize();
                    break;
                case "status": break;
                default: return Failure("VanguardAutomation", "link", "unknown do");
            }
            DroneView view = link.Linked;
            Missile drone = view?.Missile;
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            DroneFlightView flightView = DroneFlightView.Active;
            Camera droneCamera = flightView != null && flightView.Presenting ? flightView.Camera : null;
            Camera stockCamera = camera != null ? camera.mainCamera : null;
            Missile piloted = pilot != null ? pilot.Current : null;
            AvConsole console = AvConsole.Find(MfdSlots.Drones);
            var status = new Dictionary<string, object>
            {
                ["ok"] = true,
                ["count"] = link.Count,
                ["linked"] = link.LinkedIndex,
                ["linkedName"] = view?.Callsign ?? "",
                ["owned"] = link.Owns(index),
                ["shared"] = link.IsShared(index),
                ["available"] = link.CanPilot(index),
                ["piloting"] = link.Piloting,
                ["droneCamera"] = piloted != null && droneCamera != null && droneCamera != stockCamera && droneCamera.isActiveAndEnabled,
                ["aircraftCamera"] = lead != null && camera != null && camera.followingUnit == lead,
                ["stockCockpit"] = camera != null && camera.currentState == camera.cockpitState,
                ["stockCameraEnabled"] = stockCamera != null && stockCamera.isActiveAndEnabled,
                ["mapOpen"] = DynamicMap.mapMaximized,
                ["homeAlive"] = lead != null && !lead.disabled && !lead.HasEjected(),
                ["flightHud"] = hud != null && hud.Visible,
                ["hudPointer"] = hud != null && hud.PointerActive,
                ["thirdPerson"] = piloted != null && pilot.ThirdPerson,
                ["chaseBehind"] = piloted != null && droneCamera != null &&
                    Vector3.Dot(droneCamera.transform.position - piloted.transform.position,
                        Vector3.ProjectOnPlane(piloted.transform.forward, Vector3.up).normalized) < -1f,
                ["controlsEnabled"] = piloted != null && pilot.ControlsEnabled,
                ["trigger"] = DronePilot.Trigger(piloted).ToString(),
                ["weapon"] = pilot != null ? pilot.Weapon.ToString() : "",
                ["ammo"] = pilot != null ? pilot.Ammunition() : 0,
                ["locked"] = pilot != null && pilot.Lock != null,
                ["rollInput"] = pilot != null ? pilot.LastAxes.z : 0f,
                ["bank"] = drone != null ? Mathf.Abs(Mathf.DeltaAngle(0f, drone.transform.eulerAngles.z)) : 0f,
                ["quality"] = view?.Quality ?? 0f,
                ["feed"] = link.Feed != null,
                ["mfd"] = console != null && console.Root.gameObject.activeInHierarchy && console.CurrentPageName == "DRONES",
            };
            link.DescribeFeed(status);
            return status;
        }

    }
}
#endif
