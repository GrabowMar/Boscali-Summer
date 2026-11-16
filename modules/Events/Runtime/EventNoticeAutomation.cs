#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Events.Presentation;
using NOAvionics;
using TMPro;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Events.Runtime
{
    /// <summary>Reads the shared event notice and presses its rendered controls. Cabinet creation and answers stay on Nation's real probe.</summary>
    public static class EventNoticeAutomation
    {
        private static int checkpointRoot, checkpointSerial;
        private static float checkpointOpened, checkpointClose, checkpointCollapse, checkpointLeft;

        /// <summary>Wait for host snapshot, Nation presentation and the shared notice fade to finish, independent of their phased ticks.</summary>
        public static bool Ready(Dictionary<string, object> args)
        {
            Dictionary<string, object> state = Read(null);
            return state.TryGetValue("cabinetNotice", out object notice) && notice is true &&
                state.TryGetValue("expanded", out object expanded) && expanded is true &&
                state.TryGetValue("titleMatchesCard", out object title) && title is true &&
                state.TryGetValue("alpha", out object alpha) && Convert.ToSingle(alpha) >= 0.99f;
        }

        /// <summary>The displayed notice has finished closing; optionally require the real cabinet card's acknowledged answer too.</summary>
        public static bool Closed(Dictionary<string, object> args)
        {
            Dictionary<string, object> state = Read(null);
            if (!state.TryGetValue("shown", out object shown) || shown is not false ||
                !state.TryGetValue("roots", out object roots) || Convert.ToInt32(roots) != 1) return false;
            return !Bool(args, "answered") || state.TryGetValue("chosen", out object chosen) && Convert.ToInt32(chosen) >= 0 &&
                state.TryGetValue("waiting", out object waiting) && waiting is false;
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("EventNoticeAutomation", "guard", "requires a hosted single-player nomodkit scenario");
            // All scene services live on BoscaliSummer.Core (HideAndDontSave); Object.FindObjectOfType omits that owner.
            SuperEventAlert alert = Resources.FindObjectsOfTypeAll<SuperEventAlert>().FirstOrDefault(c => c != null && c.isActiveAndEnabled);
            if (alert == null) return Failure("EventNoticeAutomation", "Read", "no shared event notice service");
            try
            {
                string action = Text(args, "action") ?? "read";
                if (action == "open_evn" || action == "dismiss") Press(alert, action);
                else if (action != "read" && action != "checkpoint")
                    return Failure("EventNoticeAutomation", "Read", "unknown action " + action);

                GameObject root = Field<GameObject>(alert, "root");
                CanvasGroup group = Field<CanvasGroup>(alert, "group");
                RectTransform expanded = Field<RectTransform>(alert, "expandedPanel"), compact = Field<RectTransform>(alert, "compactPanel");
                CabinetCard card = Field<CabinetCard>(alert, "activeCabinet");
                float opened = Field<float>(alert, "openedAt"), close = Field<float>(alert, "closeAt"), collapse = Field<float>(alert, "collapseAt");
                int rootId = root != null ? root.GetInstanceID() : 0;
                bool shown = root != null && root.activeInHierarchy && group != null && group.alpha > 0.01f;
                CabinetCard request = alert.Cabinet?.Card;
                if (action == "checkpoint")
                {
                    checkpointRoot = rootId; checkpointSerial = card?.Serial ?? 0;
                    checkpointOpened = opened; checkpointClose = close; checkpointCollapse = collapse;
                    checkpointLeft = card?.SecondsLeft ?? 0f;
                }
                bool sameRoot = checkpointRoot != 0 && checkpointRoot == rootId;
                bool sameTimeline = sameRoot && checkpointSerial == (card?.Serial ?? 0) &&
                    Mathf.Abs(opened - checkpointOpened) < 0.01f && Mathf.Abs(close - checkpointClose) < 0.01f && Mathf.Abs(collapse - checkpointCollapse) < 0.01f;
                string title = Parsed(Field<TMP_Text>(alert, "title"));
                EventsMfdPanel panel = Resources.FindObjectsOfTypeAll<EventsMfdPanel>().FirstOrDefault(c => c != null && c.isActiveAndEnabled);
                AvConsole console = panel != null ? Field<AvConsole>(panel, "console") : null;
                MFDScreen screen = panel != null ? (MFDScreen)typeof(EventsMfdPanel).GetProperty("screen", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(panel) : null;
                bool evnShown = console != null && screen != null && screen.isActive && console.Root.gameObject.activeInHierarchy;
                string evnText = evnShown ? string.Join("|", console.Page(console.CurrentPage).Content.GetComponentsInChildren<TMP_Text>(false).Select(Parsed)) : "";
                return new Dictionary<string, object>
                {
                    ["ok"] = true, ["action"] = action, ["mapOpen"] = DynamicMap.mapMaximized,
                    ["serviceActive"] = alert.isActiveAndEnabled,
                    ["noticeServices"] = Resources.FindObjectsOfTypeAll<SuperEventAlert>().Count(c => c != null && c.isActiveAndEnabled),
                    ["roots"] = Resources.FindObjectsOfTypeAll<Canvas>().Count(c => c.name == "BoscaliEvents.Dispatch"),
                    ["oldCueRoots"] = Resources.FindObjectsOfTypeAll<Canvas>().Count(c => c.name == "BoscaliEvents.CabinetCue"),
                    ["oldCueComponents"] = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Count(c => c != null && c.GetType().Name == "CabinetCue"),
                    ["rootId"] = rootId, ["shown"] = shown,
                    ["alpha"] = group != null ? group.alpha : 0f,
                    ["expanded"] = shown && expanded != null && expanded.gameObject.activeInHierarchy,
                    ["compact"] = shown && compact != null && compact.gameObject.activeInHierarchy,
                    ["cabinetNotice"] = shown && card != null && Parsed(Field<TMP_Text>(alert, "stamp")) == "CABINET DECISION",
                    ["title"] = title, ["titleMatchesCard"] = card != null && title.Equals(card.Title, StringComparison.OrdinalIgnoreCase),
                    ["waiting"] = alert.Cabinet != null && alert.Cabinet.Waiting,
                    ["chosen"] = request?.Chosen ?? -2, ["serial"] = request?.Serial ?? 0,
                    ["sameRoot"] = sameRoot, ["sameTimeline"] = sameTimeline,
                    ["countdownAdvanced"] = sameTimeline && card != null && card.SecondsLeft < checkpointLeft - 1f,
                    ["openedAt"] = opened, ["closeAt"] = close, ["collapseAt"] = collapse,
                    ["noticeAge"] = Time.unscaledTime - opened,
                    ["clock"] = Parsed(Field<TMP_Text>(alert, "clock")),
                    ["compactClock"] = Parsed(Field<TMP_Text>(alert, "compactClock")),
                    ["evnShown"] = evnShown, ["evnPage"] = console?.CurrentPage ?? -1,
                    ["evnHasCard"] = request != null && evnText.IndexOf(request.Title, StringComparison.OrdinalIgnoreCase) >= 0,
                    ["evnText"] = evnText.Length > 1600 ? evnText.Substring(0, 1600) : evnText
                };
            }
            catch (Exception error) { return Failure("EventNoticeAutomation", "Read", error.GetType().Name + ": " + error.Message); }
        }

        private static void Press(SuperEventAlert alert, string action)
        {
            RectTransform compact = Field<RectTransform>(alert, "compactPanel");
            AvHit hit = null;
            if (compact != null && compact.gameObject.activeInHierarchy)
            {
                if (action == "open_evn") hit = Field<AvHit>(alert, "compactOpen");
                else
                    foreach (AvControl control in compact.GetComponentsInChildren<AvControl>(false))
                        if (control.Interactable && string.IsNullOrEmpty(control.Label)) { hit = control.GetComponentInChildren<AvHit>(); break; }
            }
            else if (action == "open_evn")
            {
                AvControl control = Field<AvControl>(alert, "expandedAction");
                if (control != null && control.isActiveAndEnabled && control.Interactable && control.Label == "OPEN EVN")
                    hit = control.GetComponentInChildren<AvHit>();
            }
            if (hit == null || !hit.isActiveAndEnabled || !hit.Interactable) throw new InvalidOperationException("notice control unavailable: " + action);
            var pointer = WindowPointer.Data();
            hit.OnPointerDown(pointer); hit.OnPointerUp(pointer); hit.OnPointerClick(pointer);
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner);

        // Pooled labels use SetCharArray; GetParsedText observes rendered glyphs without consuming TMP's backing-string flag.
        private static string Parsed(TMP_Text label) => label != null ? label.GetParsedText() : "";
    }
}
#endif
