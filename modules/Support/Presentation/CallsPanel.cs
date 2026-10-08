using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using NuclearOption.MissionEditorScripts;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>The two tabs of the OPS bezel page.</summary>
    internal enum OpsTab : byte { Fronts = 1, Perks = 2 }

    /// <summary>
    /// The OPS bezel page, in the shared console frame every other MFD panel wears (<see cref="AvConsole"/>): header, four metrics, two icon
    /// tabs and the footer that carries the page's words and the hover help. FRONTS shows the three fronts as cards with an OPEN button each
    /// (the windows are <see cref="FrontController"/>'s); PERKS lists the pilot's perks by front with ARM / FIRE, the ORDER row and four quick
    /// keys. Keys 1-2 switch tabs only while the pointer is over the page. The panel owns no policy: every figure comes from
    /// <see cref="SupportManager"/> and every press goes through <see cref="CallsController"/> or <see cref="FrontController"/>.
    /// </summary>
    internal sealed class CallsPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.2f;

        private readonly OpsPageView view = new OpsPageView();
        private readonly List<CallTile> tiles = new List<CallTile>(16);

        private SupportManager manager;
        private CallsController calls;
        private FrontController fronts;
        private ManualLogSource logger;

        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Ops, "OPS", "BoscaliOperations.Screen", preferLeft: true);
        private MFDScreen screen => installer.Screen;
        private AvConsole console;
        private AvMetric[] metrics;
        private readonly FrontCardPart[] cards = new FrontCardPart[FrontRules.FrontCount];
        private readonly Dictionary<SupportActionId, PerkRowPart> perkRows = new Dictionary<SupportActionId, PerkRowPart>(16);
        private AvRow order;
        private AvControl execute, abort;
        private readonly AvControl[] quick = new AvControl[OpsPageView.FavouriteSlots];
        private AvSection[] groups;
        private OpsTab tab = OpsTab.Fronts;

        private float nextRefresh, failedUntil;
        private bool refreshFaulted;
        private Canvas pageCanvas;
        private int aimCellX = int.MinValue, aimCellZ;
        private string aimGrid = "";
        private SupportActionId armedId;

        public void Configure(SupportManager supportManager, CallsController callsController, FrontController frontController)
        {
            manager = supportManager;
            calls = callsController;
            fronts = frontController;
            logger = installer.Log = ((ISupportHost)supportManager).Logger;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();
            console = null;
            metrics = null;
            order = null; execute = abort = null; groups = null;
            for (int i = 0; i < cards.Length; i++) cards[i] = null;
            perkRows.Clear();
            tab = OpsTab.Fronts;
            nextRefresh = failedUntil = 0f;
            pageCanvas = null;
        }

        private void OnDestroy() => ResetForScene();

        private RectTransform ConsoleRootOrNull => console != null ? console.Root : null;

        private void Update()
        {
            if (installer.Failed || manager == null || calls == null) return;
            if (!installer.Tick()) return;
            if (Time.unscaledTime < failedUntil) return; // a fault backs the page off for a moment instead of killing it

            bool visible = console != null && screen.isActive && SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            if (visible) PollTabKeys();
            if (!visible || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshInterval;
            try { Refresh(); refreshFaulted = false; }
            catch (Exception e)
            {
                failedUntil = Time.unscaledTime + 2f;
                if (!refreshFaulted) logger?.LogError("OPS refresh failed: " + e);
                refreshFaulted = true;
            }
        }

        // ---- Tab keys ---------------------------------------------------------------------------------

        /// <summary>Digit keys 1-2 pick a tab, only with the pointer over the page (in flight the keys belong to the weapons), never while typing or with a modifier held.</summary>
        private void PollTabKeys()
        {
            if (!Input.anyKeyDown) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;
            if (GameplayUI.GameIsPaused || !Application.isFocused) return;
            if (!C2Tabs.KeyAllowed(PointerOverPage(), false, InputFieldChecker.InsideInputField)) return;
            for (int d = 1; d <= 2; d++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + d) && !Input.GetKeyDown(KeyCode.Keypad0 + d)) continue;
                if ((OpsTab)d != tab) console.SetPage(d - 1);
                return;
            }
        }

        private bool PointerOverPage()
        {
            RectTransform root = ConsoleRootOrNull;
            if (root == null || !root.gameObject.activeInHierarchy) return false;
            if (pageCanvas == null) pageCanvas = root.GetComponentInParent<Canvas>();
            Camera cam = pageCanvas != null && pageCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? pageCanvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(root, Input.mousePosition, cam);
        }

        // ---- Installation ----------------------------------------------------------------

        private RectTransform BuildScreen(RectTransform rootRect, float height)
        {
            BuildConsole(rootRect, height);
            return console.Root;
        }

        /// <summary>Builds the whole page into <paramref name="root"/> without an MFD (offline render harness).</summary>
        internal void BuildForHarness(RectTransform root, float height) => BuildConsole(root, height);

        private void BuildConsole(RectTransform host, float height)
        {
            console = AvConsole.Build(host, "OPS", "SUPPORT OPERATIONS", 2, Width, height);
            AvTabBar tabs = console.Tabs((AvIcon.Flag, "FRONTS"), (AvIcon.Target, "PERKS"));
            string[] hints =
            {
                "[1] The three fronts: readiness, superiority, posture, what is being built. OPEN a front for its window.",
                "[2] Your perks by front: arm one, aim it, fire it. The ORDER row and the quick keys."
            };
            AvControl[] tabButtons = tabs.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabButtons.Length && i < hints.Length; i++) tabButtons[i].Help = hints[i];
            metrics = console.Metrics("ALLOCATION", "PERKS READY", "FRONTS AHEAD", "ALERTS");
            console.PageChanged += OnPage;

            // FRONTS: three cards.
            AvFlow p0 = console.Page(0);
            for (int i = 0; i < cards.Length; i++) cards[i] = p0.Add(new FrontCardPart(p0.Content, (Front)i, OpenFront));

            // PERKS: ORDER, quick keys, then the perks grouped by front.
            AvFlow p1 = console.Page(1);
            p1.Section(AvIcon.Target, "ORDER", "ARM A PERK · AIM · FIRE");
            order = p1.Add(new AvRow(p1.Content));
            execute = order.AddTrailing(OpsKit.Spec("FIRE", () => { if (view.Armed) calls?.Press(armedId); }, AvButtonStyle.Danger));
            abort = order.AddTrailing(OpsKit.Spec("ABORT", () => calls?.Disarm(), AvButtonStyle.Quiet));
            execute.Help = "Fire the armed perk at the aim shown. The host spends and answers.";
            abort.Help = "Disarm the armed perk. Nothing is spent.";
            var specs = new AvControl.Spec[quick.Length];
            for (int i = 0; i < quick.Length; i++) { int slot = i; specs[i] = OpsKit.Spec("", () => PressQuick(slot), AvButtonStyle.Default); }
            AvButtons qb = p1.Buttons(specs);
            for (int i = 0; i < quick.Length; i++) { quick[i] = qb.Controls[i]; quick[i].SingleLine(); }

            groups = new AvSection[FrontRules.FrontCount];
            for (int f = 0; f < FrontRules.FrontCount; f++)
            {
                var front = (Front)f;
                groups[f] = p1.Section(f == 0 ? AvIcon.Satellite : f == 1 ? AvIcon.Antenna : AvIcon.UsersGroup, FrontRules.Name(front) + " PERKS");
                foreach (CallRow r in CallSheet.Rows)
                    if (r.Front == front) perkRows[r.Id] = p1.Add(new PerkRowPart(p1.Content, id => calls?.Press(id), r.Id));
            }

            console.Finish();
            console.SetPage(0);
            OnPage(0);
        }

        private void OpenFront(Front front)
        {
            fronts?.Open(front);
        }

        private void PressQuick(int slot)
        {
            if (calls == null || slot >= calls.Favourites.Length) return;
            SupportActionId? id = calls.Favourites[slot];
            if (id.HasValue) calls.Press(id.Value);
        }

        private string KeyName(int slot)
        {
            SupportSettings s = manager?.Settings;
            var key = slot == 0 ? s?.CallKey1 : slot == 1 ? s?.CallKey2 : slot == 2 ? s?.CallKey3 : s?.CallKey4;
            if (key == null || key.Value.MainKey == KeyCode.None) return (slot + 1).ToString();
            return key.Value.ToString().Replace("Alpha", "").Replace("Keypad", "NUM ").ToUpperInvariant();
        }

        private void OnPage(int page)
        {
            tab = (OpsTab)(page + 1);
            nextRefresh = 0f;
        }

        /// <summary>Maximize the map, press the OPS bezel and open <paramref name="next"/>, as the player would (automation).</summary>
        internal void OpenForAutomation(OpsTab next)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            if (screen != null && !screen.isActive && installer.Bezel != null) installer.Bezel.onClick.Invoke();
            SelectTab(next);
        }

        internal void SelectTab(OpsTab next) => console?.SetPage((int)next - 1);

        internal bool Installed => screen != null;
        internal bool ScreenActive => screen != null && screen.isActive;
        internal OpsTab Tab => tab;
        internal AvTicker Ticker => console?.Ticker;
        internal RectTransform ConsoleRoot => ConsoleRootOrNull;
        /// <summary>The figures last painted (automation reads them back).</summary>
        internal OpsPageView View => view;

        // ---- Refresh and paint ------------------------------------------------------------

        private void Refresh()
        {
            float now = SupportManager.MissionNow();
            tiles.Clear();
            foreach (CallRow row in CallSheet.Rows)
                tiles.Add(CallsView.Tile(row, manager.Quote(row.Id), manager.LockReason(row.Id), manager.LocalAllocation,
                    manager.LocalCooldownRemaining(row.Id), calls.Armed == row.Id, false, !manager.Online));
            view.Allocation = (int)manager.LocalAllocation;
            OpsPageViews.Perks(view, tiles);
            FrontMirror fm = manager.FrontMirror;
            OpsPageViews.Cards(view, fm.State, fm.Known, now);
            OpsPageViews.Order(view, tiles, calls.AimNow, AimGridNow(), calls.Pending, calls.LastWords, view.Allocation);
            for (int i = 0; i < view.Favourites.Length; i++)
            {
                view.Favourites[i] = null;
                SupportActionId? id = i < calls.Favourites.Length ? calls.Favourites[i] : null;
                if (!id.HasValue) continue;
                foreach (CallTile t in tiles) if (t.Id == id.Value) { view.Favourites[i] = t; break; }
            }
            view.FrontWords = fronts != null && SupportManager.MissionNow() - fronts.LastWordsAt < 8f ? fronts.LastWords : "";
            view.FrontWordsTone = fronts != null ? fronts.LastWordsTone : AvState.Inert;
            Paint(view);
        }

        private string AimGridNow()
        {
            if (!calls.Armed.HasValue || !calls.TryAimPoint(out GlobalPosition p)) { aimCellX = int.MinValue; aimGrid = ""; return ""; }
            int cx = Mathf.RoundToInt((float)(p.x / 100.0)), cz = Mathf.RoundToInt((float)(p.z / 100.0));
            if (cx != aimCellX || cz != aimCellZ) { aimCellX = cx; aimCellZ = cz; aimGrid = OpsKit.Grid((float)p.x, (float)p.z); }
            return aimGrid;
        }

        /// <summary>Paints the metrics, the open page and the footer from <paramref name="v"/> (the harness feeds fixtures here).</summary>
        internal void Paint(OpsPageView v)
        {
            if (console == null) return;
            metrics[0].Set(v.Allocation.ToString(), "ALLOC", Mathf.Clamp01(v.Allocation / 100f), v.Allocation > 0 ? AvState.Ready : AvState.Caution);
            metrics[1].Set(v.PerksReady + "/" + v.PerksTotal, "READY", v.PerksTotal > 0 ? v.PerksReady / (float)v.PerksTotal : 0f, v.PerksReady > 0 ? AvState.Ready : AvState.Caution);
            metrics[2].Set(v.FrontsAhead + "/" + FrontRules.FrontCount, "AHEAD", v.FrontsAhead / (float)FrontRules.FrontCount, v.FrontsAhead > 0 ? AvState.Ready : AvState.Caution);
            metrics[3].Set(v.Alerts.ToString(), v.Alerts == 1 ? "ALERT" : "ALERTS", Mathf.Clamp01(v.Alerts / 3f), v.Alerts > 0 ? AvState.Danger : AvState.Ready);

            for (int i = 0; i < cards.Length; i++) cards[i]?.Paint(v.Cards[i]);

            armedId = default;
            foreach (PerkRowView p in v.Perks)
            {
                if (p.Armed) armedId = p.Id;
                if (perkRows.TryGetValue(p.Id, out PerkRowPart row)) row.Paint(p);
            }
            order.Set(v.ArmedName, v.AimLine, v.OrderRight, v.Armed ? AvState.Caution : v.Pending ? AvState.Info : AvState.Ready);
            order.Armed = v.Armed;
            OpsKit.Enable(execute, v.Armed);
            OpsKit.Enable(abort, v.Armed);
            for (int i = 0; i < quick.Length; i++)
            {
                CallTile? t = v.Favourites[i];
                string key = KeyName(i);
                if (t.HasValue)
                {
                    OpsKit.Label(quick[i], "[" + key + "] " + t.Value.Label);
                    OpsKit.Enable(quick[i], t.Value.Enabled);
                    quick[i].Armed = t.Value.State == CallState.Armed;
                }
                else
                {
                    OpsKit.Label(quick[i], "[" + key + "] EMPTY");
                    OpsKit.Enable(quick[i], false);
                    quick[i].Armed = false;
                }
            }
            int[] ready = new int[FrontRules.FrontCount];
            foreach (PerkRowView p in v.Perks) if (p.Enabled) ready[(int)p.Front]++;
            for (int f = 0; f < groups.Length; f++) groups[f].SetCaption(ready[f] + " READY");

            string words; AvState tone;
            if (tab == OpsTab.Perks) { words = v.Words.Length > 0 ? v.Words : "ARM A PERK, AIM, THEN FIRE"; tone = v.WordsTone; }
            else if (v.FrontWords.Length > 0) { words = v.FrontWords; tone = v.FrontWordsTone; }
            else { words = "OPEN A FRONT FOR ITS MAP, PROGRAMMES AND PERKS"; tone = AvState.Ready; }
            console.Footer.Set(words, tone);
        }
    }
}
