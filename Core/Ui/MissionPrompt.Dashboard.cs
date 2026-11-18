using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using HarmonyLib;
using NOAvionics;
using NuclearOption.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Ui
{
    internal sealed partial class MissionPrompt
    {
        private static readonly FieldInfo MenuModeField = AccessTools.Field(typeof(LeaderboardMenu), "currentMode");
        private static readonly FieldInfo SettingsButtonField = AccessTools.Field(typeof(LeaderboardMenu), "settingsButton");
        private static readonly FieldInfo PrimaryButtonField = AccessTools.Field(typeof(LeaderboardMenu), "primaryActionButton");
        private static readonly FieldInfo QuitPanelField = AccessTools.Field(typeof(QuitMissionButton), "confirmLeavePanel");
        internal static MissionPrompt Active { get; private set; }
        internal static bool DashboardOpen => Active != null && Active.window != null;
        internal static bool DashboardVisible => Active != null && Active.window != null && Active.window.Root.gameObject.activeInHierarchy;
        private LeaderboardMenu nativeMenu;
        private QuitMissionButton nativeQuit;
        private GameObject nativeQuitPanel;
        private CanvasGroup nativeGroup;
        private bool ownsNativeGroup, originalInteractable, originalRaycasts;
        private float originalAlpha;
        private Split systems;
        private DashboardDeck deck;
        private AvControl deployTab, systemsTab;
        private AvHit keyboardFocus;
        private bool systemsPage;
        private float nextDashboardTick;

        private void UpdateDashboard()
        {
            LeaderboardMenu menu = SceneSingleton<LeaderboardMenu>.i;
            bool join = menu != null && menu.gameObject.activeInHierarchy && MenuModeField != null &&
                (MenuMode)MenuModeField.GetValue(menu) == MenuMode.Join;
            string name = join ? MissionManager.CurrentMission?.Name : BoscaliMission.PendingMission;
            bool wanted = MissionManager.IsRunning && name != null && Cursor.lockState != CursorLockMode.Locked;
            if (window != null && (!wanted || mission != name || (join && nativeMenu != menu))) Close();
            if (wanted && window == null)
            {
                nativeMenu = join ? menu : null;
                nativeQuit = join ? menu.GetComponentInChildren<QuitMissionButton>(true) : null;
                nativeQuitPanel = nativeQuit != null ? QuitPanelField?.GetValue(nativeQuit) as GameObject : null;
                Open(name);
                if (!join) SelectPage(true);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
            if (window == null) return;
            bool modal = NativeModal();
            window.Root.gameObject.SetActive(!modal);
            SetNativeHidden(!modal);
            if (modal) return;
            if (Time.unscaledTime >= nextDashboardTick)
            {
                nextDashboardTick = Time.unscaledTime + .25f;
                deck?.Refresh();
            }
            // Menu-only keyboard navigation; never allows a hidden native button to receive Submit.
            if (Input.anyKeyDown)
                foreach (var binding in window.Root.GetComponentsInChildren<KeyBindControl>())
                    if (binding.ConsumingKeyboard) return;
            if (Input.GetKeyDown(KeyCode.Tab)) MoveFocus(Input.GetKey(KeyCode.LeftShift) ? -1 : 1);
            if (Input.GetKeyDown(KeyCode.Return) && keyboardFocus != null && keyboardFocus.Interactable)
                keyboardFocus.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            if (Input.GetKeyDown(KeyCode.Escape) && systemsPage) SelectPage(false);
        }

        private bool NativeModal()
        {
            if (nativeMenu == null) return false;
            if (nativeMenu.SettingsMenuOpen) return true;
            return nativeQuitPanel != null && nativeQuitPanel.activeSelf;
        }

        private void SetNativeHidden(bool hidden)
        {
            if (nativeMenu == null) return;
            if (nativeGroup == null)
            {
                nativeGroup = nativeMenu.GetComponent<CanvasGroup>();
                ownsNativeGroup = nativeGroup == null;
                if (ownsNativeGroup) nativeGroup = nativeMenu.gameObject.AddComponent<CanvasGroup>();
                originalAlpha = nativeGroup.alpha;
                originalInteractable = nativeGroup.interactable;
                originalRaycasts = nativeGroup.blocksRaycasts;
            }
            nativeGroup.alpha = hidden ? 0f : originalAlpha;
            nativeGroup.interactable = !hidden && originalInteractable;
            nativeGroup.blocksRaycasts = !hidden && originalRaycasts;
        }

        private void RestoreNative()
        {
            if (nativeGroup != null)
            {
                nativeGroup.alpha = originalAlpha;
                nativeGroup.interactable = originalInteractable;
                nativeGroup.blocksRaycasts = originalRaycasts;
                if (ownsNativeGroup) Destroy(nativeGroup);
            }
            nativeGroup = null;
            nativeQuit = null;
            nativeQuitPanel = null;
            keyboardFocus = null;
        }

        private void BuildDashboard()
        {
            systemsPage = false;
            var tabs = new AvButtons(window.Root, new[] {
                new AvControl.Spec("01  /  DEPLOYMENT", () => SelectPage(false), AvButtonStyle.Tab, AvIcon.Runway),
                new AvControl.Spec("02  /  BOSCALI SYSTEMS", () => SelectPage(true), AvButtonStyle.Tab, AvIcon.AdjustmentsHorizontal) });
            tabs.MinHeight = 44f;
            tabs.Place(new AvSlot(26, 44, 704, 44));
            window.Ticker.Register(tabs);
            // Navigation stays fixed while the feature catalogue scrolls beneath it.
            var scroll = window.Body.Content.GetComponentInParent<ScrollRect>(true);
            float bodyHeight = Height - 106 - AvGridTokens.Footer;
            AvLay.Place((RectTransform)scroll.transform, 0, 106, Width, bodyHeight);
            AvLay.Place(scroll.viewport, 0, 0, Width, bodyHeight);
            if (scroll.verticalScrollbar != null)
                ((RectTransform)scroll.verticalScrollbar.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bodyHeight - 4);
            deployTab = tabs.Controls[0]; systemsTab = tabs.Controls[1];
            SizeControl(deployTab); SizeControl(systemsTab);
            deployTab.Accent = systemsTab.Accent = new Color(.72f, .85f, .93f);
            deployTab.Latched = true;
            deck = window.Body.Add(new DashboardDeck(this, window.Body.Content));
        }

        private void SelectPage(bool features)
        {
            systemsPage = features;
            if (keyboardFocus != null) keyboardFocus.Hover?.Invoke(false);
            keyboardFocus = null;
            deployTab.Latched = !features; systemsTab.Latched = features;
            deck.SetShown(!features); systems.SetShown(features);
            window.Body.Relayout();
            var scroll = window.Body.Content.GetComponentInParent<ScrollRect>(true);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
            window.Footer.Set(features
                ? "SYSTEMS  /  Changes are staged until APPLY or DEPLOY. MODE LOCK follows this mission's rules."
                : "MISSION CONTROL  /  Select a faction to deploy. Tab / Enter: navigate. Esc: return from systems.");
        }

        private void MoveFocus(int direction)
        {
            var controls = new List<AvHit>();
            foreach (var control in window.Root.GetComponentsInChildren<AvHit>())
                if (control.Interactable && control.gameObject.activeInHierarchy) controls.Add(control);
            if (controls.Count == 0) return;
            int index = controls.IndexOf(keyboardFocus);
            if (keyboardFocus != null) keyboardFocus.Hover?.Invoke(false);
            keyboardFocus = controls[(index + direction + controls.Count) % controls.Count];
            keyboardFocus.Hover?.Invoke(true);
            var scroll = keyboardFocus.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.content != null)
            {
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, keyboardFocus.transform);
                Rect visible = scroll.viewport.rect;
                float offset = bounds.max.y > visible.yMax ? visible.yMax - bounds.max.y :
                    bounds.min.y < visible.yMin ? visible.yMin - bounds.min.y : 0;
                scroll.StopMovement();
                scroll.content.anchoredPosition += new Vector2(0, offset);
            }
        }

        private void Join(FactionHQ faction)
        {
            if (nativeMenu == null || faction == null || faction.preventJoin) return;
            ApplyDeploymentSelection();
            LeaderboardMenu menu = nativeMenu;
            Close();
            menu.JoinFactionCallback(faction);
        }

        private void Spectate()
        {
            if (nativeMenu == null) { Done(false, false); return; }
            var button = PrimaryButtonField?.GetValue(nativeMenu) as Button;
            if (button == null) return;
            ApplyDeploymentSelection();
            Close();
            button.onClick.Invoke();
        }

        private void OpenSettings()
        {
            if (nativeMenu != null && SettingsButtonField?.GetValue(nativeMenu) is Button button)
                button.onClick.Invoke();
        }

        private void ApplyDeploymentSelection()
        {
            // Simply joining an authored mission must not overwrite the pilot's saved preferences.
            if (BoscaliMission.PendingMission != null || !off.SetEquals(initialOff))
                BoscaliMission.Confirm(mission, off, false, false);
        }

        private void QuitMission()
        {
            var button = nativeQuit != null ? nativeQuit.GetComponent<Button>() : null;
            if (button != null) button.onClick.Invoke();
        }

        private static void SizeControl(AvControl control)
        {
            var label = control.Rect.Find("Label").GetComponent<TMP_Text>();
            AvType.Apply(label, AvTextRole.Title);
            label.enableAutoSizing = true;
            label.fontSize = label.fontSizeMax = 20;
            label.fontSizeMin = 18;
            label.rectTransform.offsetMin = new Vector2(control.Glyph != null ? 48 : 16, 0);
            label.rectTransform.offsetMax = new Vector2(-16, 0);
            if (control.Glyph != null)
            {
                control.Glyph.fontSize = 22;
                control.Glyph.rectTransform.sizeDelta = new Vector2(24, 24);
                control.Glyph.rectTransform.anchoredPosition = new Vector2(16, 0);
            }
            control.Bracket = 8;
        }

        /// <summary>A single measured page, built with the shared avionics font, controls and vector frames.</summary>
        private sealed class DashboardDeck : AvPart
        {
            private const float DesignWidth = 1648f, DesignHeight = 756f;
            private static readonly Color Mint = new Color(.48f, .83f, .73f);
            private static readonly Color Amber = new Color(.91f, .66f, .44f);
            private static readonly Color Ink = new Color(.91f, .95f, .98f);
            private static readonly Color Muted = new Color(.66f, .76f, .83f);
            private static readonly Color Edge = new Color(.48f, .62f, .72f, .32f);
            private static readonly Color Surface = new Color(.09f, .13f, .17f, .58f);
            private readonly MissionPrompt owner;
            private readonly RectTransform layout;
            private readonly TMP_Text title, link, phase, clock, score, objective, detail, systemCount, systemNote;
            private readonly FactionCard[] factions = new FactionCard[2];
            private readonly AvControl moreFactions;
            private LeaderboardFactionItem[] factionItems = Array.Empty<LeaderboardFactionItem>();
            private int factionOffset;

            public DashboardDeck(MissionPrompt owner, RectTransform parent)
            {
                this.owner = owner;
                Rect = AvLay.Child(parent, "Deployment");
                layout = AvLay.Child(Rect, "InstrumentPanel");
                title = Text(layout, owner.mission.ToUpperInvariant(), 64, Ink, 16, 2, 1140, 80, role: AvTextRole.Headline);
                Text(layout, "Select a faction. Configure your systems. Enter the battlespace.", 22, Muted, 20, 86, 1120, 34);
                link = Text(layout, "", 15, Muted, 1160, 22, 468, 68, TextAlignmentOptions.TopRight, AvTextRole.DataSmall);
                Line(layout, 20, 136, 1608, Edge);
                Text(layout, "SELECT YOUR SIDE", 20, Ink, 20, 154, 960, 28, role: AvTextRole.Head);
                Text(layout, "MISSION BRIEF", 20, Ink, 1048, 154, 576, 28, role: AvTextRole.Head);
                factions[0] = new FactionCard(this, 0, 0, Mint);
                factions[1] = new FactionCard(this, 1, 500, Amber);
                var report = Panel(layout, "LiveBrief", 1024, 196, 624, 350, Muted);
                phase = Text(report, "CONNECTING", 18, Muted, 24, 20, 376, 30, role: AvTextRole.Head);
                clock = Text(report, "--:--", 26, Ink, 416, 20, 184, 34, TextAlignmentOptions.TopRight, AvTextRole.Data);
                score = Text(report, "", 32, Ink, 24, 72, 576, 50, role: AvTextRole.Title);
                Line(report, 24, 138, 576, Edge);
                objective = Text(report, "", 25, Ink, 24, 156, 576, 106);
                detail = Text(report, "", 20, Muted, 24, 276, 576, 54);
                moreFactions = Button(layout, "NEXT FACTIONS", () => { factionOffset += 2; BindFactions(); }, 784, 148, 216, 40, AvIcon.ArrowRight, Muted);
                Text(layout, "MISSION CONFIGURATION", 20, Ink, 20, 566, 1648, 28, role: AvTextRole.Head);
                var load = Panel(layout, "SystemsSummary", 0, 608, 484, 80, Muted);
                systemCount = Text(load, "", 23, Ink, 24, 10, 436, 32, role: AvTextRole.Data);
                systemNote = Text(load, "", 20, Muted, 24, 44, 436, 26);
                var systems = Button(layout, "BOSCALI SYSTEMS", () => owner.SelectPage(true), 500, 608, 548, 80, AvIcon.AdjustmentsHorizontal, Muted);
                systems.Help = "Avionics, wingmen, support, weather, Spooktober and every offered module.";
                var controls = Button(layout, "FLIGHT SETTINGS", owner.OpenSettings, 1064, 608, 584, 80, AvIcon.Gauge, Muted);
                controls.Interactable = owner.nativeMenu != null;
                Button(layout, owner.nativeMenu == null ? "RETURN TO MISSION" : "SPECTATE", owner.Spectate, 0, 700, 280, 48, AvIcon.Eye, Muted);
                Text(layout, "BOSCALI SUMMER  /  " + Plugin.PluginVersion + "    ·    READY WHEN YOU ARE", 17, Muted, 308, 708, 1000, 30, role: AvTextRole.DataSmall);
                var quit = Button(layout, "QUIT MISSION", owner.QuitMission, 1400, 700, 248, 48, AvIcon.X, Muted, AvButtonStyle.Danger);
                quit.Interactable = owner.nativeMenu != null;
                BindFactions();
                Refresh();
            }

            private void BindFactions()
            {
                var items = new List<LeaderboardFactionItem>();
                if (owner.nativeMenu != null)
                    foreach (var item in owner.nativeMenu.GetComponentsInChildren<LeaderboardFactionItem>(true))
                        if (item.HQ != null) items.Add(item);
                factionItems = items.ToArray();
                if (factionOffset >= factionItems.Length) factionOffset = 0;
                for (int i = 0; i < 2; i++) factions[i].Bind(factionOffset + i < factionItems.Length ? factionItems[factionOffset + i] : null);
                moreFactions.Rect.gameObject.SetActive(factionItems.Length > 2);
            }

            public void Refresh()
            {
                if (owner.nativeMenu != null && factionItems.Length == 0) BindFactions();
                foreach (var faction in factions) faction.Refresh();
                AvText.Set(link, (GameManager.gameState == GameState.Multiplayer ? "MULTIPLAYER" : "SINGLE PLAYER") +
                    "  /  " + (GameAccess.IsServer() ? "HOST" : "CLIENT") + "\nLOCAL " + DateTime.Now.ToString("HH:mm") + "  /  MISSION LINK ACTIVE");
                bool has = ModuleServices.TryGet<IMissionDashboardView>(out var view) && view.TryRead(out _);
                if (has && view.TryRead(out var state))
                {
                    AvText.Set(title, state.Name.ToUpperInvariant());
                    AvText.Set(phase, state.Phase);
                    AvText.Set(clock, state.HasTimer ? TimeSpan.FromSeconds(Mathf.Max(0, state.RemainingSeconds)).ToString(@"mm\:ss") : state.Ready ? "LIVE" : "--:--");
                    if (!state.Ready) AvText.Set(link, "MISSION LINK\nAWAITING HOST REPORT");
                    AvText.Set(score, state.HasScores ? "BDF  " + state.Score0.ToString("00") + "    :    " + state.Score1.ToString("00") + "  PALA" : "MISSION BRIEFING");
                    AvText.Set(objective, state.Objective);
                    AvText.Set(detail, string.IsNullOrEmpty(state.Route) ? state.Detail : state.Route + "\n" + state.Detail);
                }
                else
                {
                    AvText.Set(phase, "MISSION IN PROGRESS"); AvText.Set(clock, "LIVE");
                    AvText.Set(score, "YOUR NEXT SORTIE");
                    string description = MissionManager.CurrentMission?.missionSettings?.description;
                    if (string.IsNullOrWhiteSpace(description)) description = "Choose your faction, configure Boscali systems and enter the tactical map to plan your sortie.";
                    AvText.Set(objective, description);
                    AvText.Set(detail, "Faction selection opens the tactical map. Aircraft and airbase selection follow there.");
                }
                int count = 0;
                foreach (var module in owner.Offered) if (!owner.off.Contains(module.Id)) count++;
                AvText.Set(systemCount, count + " / " + owner.Offered.Length + " SYSTEMS SELECTED");
                AvText.Set(systemNote, "Selection applies when you deploy.");
            }

            public override float Measure(float width) => DesignHeight * width / DesignWidth;
            public override void Place(AvSlot slot)
            {
                base.Place(slot);
                AvLay.Place(layout, 0, 0, DesignWidth, DesignHeight);
                layout.localScale = Vector3.one * (slot.W / DesignWidth);
            }

            private sealed class FactionCard
            {
                private readonly MissionPrompt owner;
                private readonly TMP_Text name, status, stats;
                private readonly Image logo;
                private readonly AvControl join;
                private LeaderboardFactionItem item;
                public FactionCard(DashboardDeck deck, int index, float x, Color accent)
                {
                    owner = deck.owner;
                    var panel = Panel(deck.layout, "Faction " + index, x, 196, 484, 350, accent);
                    var scope = AvLay.Child(panel, "Scope");
                    AvLay.Place(scope, 302, 56, 158, 158);
                    var graphic = scope.gameObject.AddComponent<DashboardScope>();
                    graphic.color = accent; graphic.raycastTarget = false;
                    logo = AvLay.Solid(panel, "FactionInsignia", Color.white);
                    logo.preserveAspect = true; logo.raycastTarget = false;
                    AvLay.Place(logo.rectTransform, 324, 78, 114, 114);
                    status = Text(panel, "", 15, accent, 24, 18, 436, 28, role: AvTextRole.DataSmall);
                    name = Text(panel, "", 34, Ink, 24, 72, 280, 120, role: AvTextRole.Title);
                    Line(panel, 24, 224, 436, Edge);
                    stats = Text(panel, "", 18, Muted, 24, 242, 436, 28, role: AvTextRole.Data);
                    join = Button(panel, "DEPLOY", () => { if (item != null) owner.Join(item.HQ); }, 24, 286, 436, 48, AvIcon.Plane, accent, AvButtonStyle.Primary);
                }
                public void Bind(LeaderboardFactionItem source)
                {
                    item = source;
                    logo.sprite = item != null ? item.HQ.faction.factionColorLogo : null;
                    logo.enabled = logo.sprite != null;
                    AvText.Set(name, item != null ? item.HQ.faction.factionExtendedName : "AWAITING FACTION");
                    Refresh();
                }
                public void Refresh()
                {
                    bool available = item != null && item.HQ != null && !item.HQ.preventJoin;
                    join.Interactable = available;
                    join.Label = available ? "DEPLOY  /  " + item.HQ.faction.factionName.ToUpperInvariant() : "UNAVAILABLE";
                    AvText.Set(status, available ? "ENLISTMENT OPEN" : "ENLISTMENT CLOSED");
                    AvText.Set(stats, item != null && item.HQ != null ? item.HQ.GetPlayers(false).Count + " PILOTS    /    " + item.HQ.factionScore.ToString("0") + " SCORE" : "No faction available in this slot.");
                }
            }

            private static RectTransform Panel(RectTransform parent, string name, float x, float y, float w, float h, Color accent)
            {
                var root = AvLay.Child(parent, name); AvLay.Place(root, x, y, w, h);
                var frame = AvFrame.Add(root, "InstrumentFrame", AvChamfer.Diagonal(6));
                AvLay.Fill(frame.rectTransform); frame.Paint(Surface, Edge); frame.Bracket = 10; frame.BracketColor = Muted.WithAlpha(.5f);
                AvSurfaceGradient.Apply(frame, Color.white, new Color(.76f, .84f, .91f, .9f));
                Line(root, 24, 0, 36, accent.WithAlpha(.7f));
                return root;
            }
            private static void Line(RectTransform parent, float x, float y, float w, Color color)
            {
                var line = AvLay.Solid(parent, "Rule", color); line.raycastTarget = false;
                AvLay.Place(line.rectTransform, x, y, w, 1.5f);
            }
            private static TMP_Text Text(RectTransform parent, string value, float size, Color color, float x, float y, float w, float h, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, AvTextRole role = AvTextRole.Prose)
            {
                var text = AvText.Make(parent, "Readout", role, value, alignment, true);
                text.fontSize = size; text.color = color; text.richText = false; text.raycastTarget = false;
                text.enableAutoSizing = true; text.fontSizeMax = size; text.fontSizeMin = Mathf.Min(20, size);
                text.overflowMode = TextOverflowModes.Ellipsis;
                AvLay.Place(text.rectTransform, x, y, w, h);
                return text;
            }
            private static AvControl Button(RectTransform parent, string label, Action click, float x, float y, float w, float h, AvIcon icon, Color accent, AvButtonStyle style = AvButtonStyle.Default)
            {
                var button = AvControl.Make(parent, new AvControl.Spec(label, click, style, icon));
                SizeControl(button);
                button.Accent = accent; AvLay.Place(button.Rect, x, y, w, h);
                return button;
            }
        }
    }

    /// <summary>Static insignia registration marks, with no simulated sensor data or update loop.</summary>
    internal sealed class DashboardScope : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 centre = r.center;
            float radius = Mathf.Min(r.width, r.height) * .46f;
            Color line = color; line.a = .17f;
            for (int i = 0; i < 72; i++)
            {
                float a = i * Mathf.PI / 36, b = (i + 1) * Mathf.PI / 36;
                Segment(vh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                    centre + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, .7f, line);
            }
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Segment(vh, centre + axis * (radius - 4), centre + axis * (radius + 4), 1, line);
            }
        }
        private static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * width * .5f;
            int start = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
