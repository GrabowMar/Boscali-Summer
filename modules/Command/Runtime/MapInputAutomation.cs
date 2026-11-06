#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Sim hook for the 2026-10-29 map input rework (rule B): opens the big map, selects tracks for the deck, arms a stand-in tool
    /// for the banner and the pause hold, and opens the context card at the viewport centre. Every call returns what the surface
    /// shows (<c>deck</c>, <c>deckCount</c>, <c>keys</c>, <c>banner</c>, <c>pauseHeld</c>, <c>pauseKey</c>, <c>card</c>,
    /// <c>cardActions</c>, <c>cardFacts</c>, <c>cardHeight</c>, <c>rightRule</c>, <c>selected</c>). Hosted single-player sims only.
    /// </summary>
    public static class MapInputAutomation
    {
        private const string TestOwner = "boscali.test.tool";

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("MapInputAutomation", "guard", "requires a hosted single-player nomodkit scenario");
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return Failure("MapInputAutomation", "Step", "no map");
            switch (Text(args, "do"))
            {
                case "open":
                    if (!DynamicMap.mapMaximized) map.Maximize();
                    break;
                case "close":
                    if (DynamicMap.mapMaximized) map.Minimize();
                    break;
                case "select":
                    Select(map, Mathf.RoundToInt(Number(args, "count", 4f)));
                    break;
                case "clear":
                    MfdMapInteractions.ClearSelectionTool();
                    break;
                case "arm":
                    if (!MapPicker.TryArm(TestOwner, MapPicker.GestureRight, "TEST TOOL · RIGHT-CLICK THE MAP"))
                        return Failure("MapInputAutomation", "arm", "the map picker is busy");
                    break;
                case "disarm":
                    MapPicker.Disarm(TestOwner);
                    break;
                case "card":
                    if (!OpenCard(map, Bool(args, "onTrack"))) return Failure("MapInputAutomation", "card", "no ground or track to open on");
                    break;
                // Measuring the open map: lift vsync (a hidden sim window otherwise stalls for seconds), profile the map code, move the view.
                case "uncap":
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                    break;
                case "profile":
                    Core.Diagnostics.FootprintProfiler.Watch = Text(args, "watch") ?? "";
                    return new Dictionary<string, object> { ["mode"] = Core.Diagnostics.FootprintProfiler.Start(Bool(args, "deep")) };
                case "reset":
                    Core.Diagnostics.FootprintProfiler.ResetWindow();
                    break;
                case "snapshot":
                    return new Dictionary<string, object> { ["snapshot"] = Core.Diagnostics.FootprintProfiler.SnapshotJson() };
                case "turn":
                    ReliefNavigator.Turn(Number(args, "degrees", 30f));
                    ReliefNavigator.Tilt(Number(args, "tilt", 0f));
                    break;
                case "fit":
                    ReliefNavigator.Fit();
                    break;
                case "camerapath":
                    MfdTerrainRelief.DirectDrawOff = Bool(args, "on");
                    break;
                case "focus":
                    if (GameManager.GetLocalAircraft(out Aircraft flown) && flown != null) ReliefNavigator.Focus(flown.GlobalPosition(), true);
                    break;
                // A native nuclear exclusion zone sourced from the player's own aircraft, 8 km ahead (the circle lives while it does).
                case "zone":
                    if (!GameManager.GetLocalAircraft(out Aircraft source) || source == null || map.HQ == null)
                        return Failure("MapInputAutomation", "zone", "no aircraft or HQ");
                    map.HQ.AddExclusionZone(source, (source.transform.position + source.transform.forward * 8000f).ToGlobalPosition(),
                        Number(args, "radius", 3000f));
                    break;
                case "zones":
                    var zones = new System.Text.StringBuilder();
                    foreach (NuclearOption.ExclusionZone zone in map.HQ != null ? map.HQ.GetExclusionZones() : new List<NuclearOption.ExclusionZone>())
                        zones.Append($"{zone.position.x:0}/{zone.position.z:0}/{zone.radius:0} ");
                    GameManager.GetLocalAircraft(out Aircraft own);
                    GlobalPosition at = own != null ? own.GlobalPosition() : default;
                    return new Dictionary<string, object> { ["zones"] = zones.ToString(), ["player"] = $"{at.x:0}/{at.z:0}" };
            }
            return State(map);
        }

        private static void Select(DynamicMap map, int count)
        {
            GameManager.GetLocalAircraft(out Aircraft own);
            int taken = 0;
            for (int i = 0; i < map.mapIcons.Count && taken < count; i++)
            {
                if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null || icon.unit.disabled || icon.unit == own ||
                    !icon.gameObject.activeInHierarchy || map.selectedIcons.Contains(icon)) continue;
                map.SelectIcon(icon.unit);
                taken++;
            }
        }

        /// <summary>The card as a right click at the viewport centre (or on a shown track) would open it, over the ground 3 km off the
        /// player's nose: the screen point picks the track, the ground point fills the facts.</summary>
        private static bool OpenCard(DynamicMap map, bool onTrack)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft own) || own == null) return false;
            Canvas canvas = map.mapBackground.canvas;
            Camera camera = MapUiPointer.UiCamera(canvas);
            RectTransform viewport = map.mapBackground.rectTransform;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, viewport.TransformPoint(viewport.rect.center));
            if (onTrack)
                for (int i = 0; i < map.mapIcons.Count; i++)
                {
                    if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null || icon.unit == own || icon.iconImage == null ||
                        !icon.iconImage.gameObject.activeInHierarchy) continue;
                    Vector2 at = RectTransformUtility.WorldToScreenPoint(camera, icon.iconImage.transform.position);
                    if (!MapUiPointer.Contains(viewport, at) || MapUiPointer.OverControls(at)) continue;
                    screen = at;
                    break;
                }
            Vector3 nose = own.transform.forward;
            GlobalPosition ground = own.GlobalPosition() + new Vector3(nose.x, 0f, nose.z).normalized * 3000f;
            MfdMapInteractions.OpenCardForTest(map, screen, ground);
            return MapContextCard.Visible;
        }

        private static Dictionary<string, object> State(DynamicMap map) => new Dictionary<string, object>
        {
            ["open"] = DynamicMap.mapMaximized ? 1f : 0f,
            ["deck"] = MapSelectionDeck.Shown ? 1f : 0f,
            ["deckCount"] = MapSelectionDeck.CountText ?? "",
            ["deckFolded"] = MapSelectionDeck.Folded ? 1f : 0f,
            ["banner"] = MfdMapInteractions.BannerShown ? 1f : 0f,
            ["pauseHeld"] = MfdMapInteractions.PauseHeld ? 1f : 0f,
            ["pauseKey"] = GameplayUI.AllowPauseKeybind ? 1f : 0f,
            ["card"] = MapContextCard.Visible ? 1f : 0f,
            ["cardActions"] = (float)MapContextCard.ShownActions,
            ["cardFacts"] = (float)MapContextCard.ShownFacts,
            ["cardHeight"] = MapContextCard.ShownHeight,
            ["rightRule"] = MfdMapInteractions.RightRule(map).ToString(),
            ["selected"] = (float)(map.selectedIcons != null ? map.selectedIcons.Count : 0),
            ["windowHoldsMouse"] = MapUiPointer.WindowHoldsMouse() ? 1f : 0f,
            ["pose"] = MfdTerrainRelief.IsDrawing ? AvNum.Fixed(MfdTerrainRelief.Yaw, 2) + "/" + AvNum.Fixed(MfdTerrainRelief.Pitch, 2) + "/" +
                AvNum.Fixed(MfdTerrainRelief.Zoom, 3) + "/" + AvNum.Fixed(MfdTerrainRelief.Rig.FocusX, 2) + "/" + AvNum.Fixed(MfdTerrainRelief.Rig.FocusZ, 2) : "",
            ["directOff"] = MfdTerrainRelief.DirectDrawOff ? 1f : 0f,
        };
    }
}
#endif
