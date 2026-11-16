#if !PUBLIC_RELEASE
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using NOAvionics;
using NuclearOption.Networking;
using NuclearOption.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Core.Ui
{
    internal static class MissionDashboardAutomation
    {
        public static object State(Dictionary<string, object> args) => MissionPrompt.ReadDashboardState();
        public static object Action(Dictionary<string, object> args) => MissionPrompt.DashboardAction(args);
    }

    internal sealed partial class MissionPrompt
    {
        internal static object ReadDashboardState()
        {
            var active = Active;
            var menu = SceneSingleton<LeaderboardMenu>.i;
            int roots = 0;
            bool modeOverlay = false;
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                if (canvas.name == "Boscali / Gamemode status" && canvas.gameObject.activeInHierarchy && canvas.enabled)
                    modeOverlay = true;
            foreach (var ticker in Resources.FindObjectsOfTypeAll<AvTicker>())
                if (ticker.gameObject.name == "AvWindow mission-dashboard") roots++;
            GameManager.GetLocalPlayer<Player>(out var player);
            return new Dictionary<string, object>
            {
                ["visible"] = DashboardVisible,
                ["page"] = active != null && active.systemsPage ? "features" : "operations",
                ["rootCount"] = roots,
                ["ownerId"] = active != null && active.window != null ? active.window.Root.GetInstanceID() : 0,
                ["mode"] = MissionManager.CurrentMission?.Name ?? "",
                ["faction"] = player != null && player.HQ != null ? player.HQ.faction.factionName : "",
                ["nativeHidden"] = active != null && active.nativeGroup != null && active.nativeGroup.alpha == 0 && !active.nativeGroup.blocksRaycasts,
                ["settingsOpen"] = menu != null && menu.SettingsMenuOpen,
                ["modeOverlayVisible"] = modeOverlay,
                ["width"] = Screen.width, ["height"] = Screen.height,
                ["effectsAllowed"] = MissionProfile.AllowsModule("effects"),
                ["stagedEffectsOn"] = active != null && !active.off.Contains("effects"),
                ["requiredLocked"] = MissionProfile.RequiredOff("weather") && !MissionProfile.AllowsModule("weather")
            };
        }

        internal static object DashboardAction(Dictionary<string, object> args)
        {
            string action = args != null && args.TryGetValue("action", out var raw) ? raw.ToString() : "";
            var active = Active;
            var menu = SceneSingleton<LeaderboardMenu>.i;
            switch (action)
            {
                case "open": SceneSingleton<GameplayUI>.i.ShowJoinMenu(); break;
                case "resize": Screen.SetResolution(System.Convert.ToInt32(args["width"]), System.Convert.ToInt32(args["height"]), FullScreenMode.Windowed); break;
                case "features": ClickDashboard("02  /  BOSCALI SYSTEMS"); break;
                case "operations": ClickDashboard("01  /  DEPLOYMENT"); break;
                case "settings": ClickDashboard("FLIGHT SETTINGS"); break;
                case "close_settings":
                    var settings = menu != null ? SettingsMenuField.GetValue(menu) as GameObject : null;
                    if (settings != null) Destroy(settings);
                    break;
                case "join_bdf": ClickDashboard("DEPLOY  /  BOSCALI"); break;
                case "join_pala": ClickDashboard("DEPLOY  /  PRIMEVA"); break;
                case "spectate": ClickDashboard("SPECTATE"); break;
                case "quit": ClickDashboard("QUIT MISSION"); break;
                case "all_on": ClickDashboard("ENABLE AVAILABLE SYSTEMS"); break;
                case "effects_off": active?.Set("effects", false); active?.RefreshCells(); break;
                case "apply": ClickDashboard("APPLY SYSTEMS / RETURN TO DEPLOYMENT"); break;
            }
            return ReadDashboardState();
        }

        private static void ClickDashboard(string label)
        {
            if (!DashboardVisible) throw new System.InvalidOperationException("Dashboard is not visible: " + label);
            foreach (var control in Active.window.Root.GetComponentsInChildren<AvControl>())
            {
                if (control.Label != label || !control.Interactable) continue;
                Canvas.ForceUpdateCanvases();
                // nomodkit disables native event processing to isolate the sandbox from desktop input.
                // Its scene EventSystem can still raycast synchronously without enabling that input.
                var events = EventSystem.current != null ? EventSystem.current : FindObjectOfType<EventSystem>(true);
                if (events == null) throw new System.InvalidOperationException("No scene EventSystem for dashboard click.");
                var corners = new Vector3[4];
                control.Rect.GetWorldCorners(corners);
                var pointer = new PointerEventData(events)
                {
                    position = (corners[0] + corners[2]) * .5f,
                    button = PointerEventData.InputButton.Left, eligibleForClick = true
                };
                var hits = new List<RaycastResult>();
                events.RaycastAll(pointer, hits);
                if (hits.Count == 0 || !hits[0].gameObject.transform.IsChildOf(control.transform))
                    throw new System.InvalidOperationException("Dashboard control is blocked: " + label);
                var target = hits[0].gameObject;
                ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
                if (ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler) == null)
                    throw new System.InvalidOperationException("Dashboard control has no click handler: " + label);
                return;
            }
            throw new System.InvalidOperationException("Dashboard control unavailable: " + label);
        }
    }
}
#endif
