using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using NuclearOption.MissionEditorScripts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>The two tabs of the OPS bezel page.</summary>
    internal enum OpsTab : byte { Fronts = 1, Perks = 2 }

    /// <summary>
    /// The OPS bezel page, in the shared console frame every other MFD panel wears (<see cref="AvConsole"/>): header, the country strip, two icon
    /// tabs and the footer that carries the page's live state and the hover help. FRONTS shows the three fronts as cards (status stamp,
    /// superiority, directive, queue, then the blocker or the programme in hand) with an OPEN button each (the windows are <see cref="FrontController"/>'s);
    /// PERKS lists the pilot's perks by front as call cards (clicking one ARMS it: arming spends nothing). A strip pinned under the tabs (outside
    /// the scroller) shows the armed or inspected perk and carries ARM / FIRE / DISARM, so those never scroll away; the cruise block (ADD LEG,
    /// CANCEL PICK, CLEAR ROUTE) heads the PERKS page while a salvo flies. The posted strike packages are jobs on MIS JOBS (<see cref="SupportJobs"/>).
    /// Keys 1-2 switch tabs only while the pointer is over the page, and never a key a favourite is bound to. The panel owns no policy: every figure
    /// comes from <see cref="SupportManager"/> and every press goes through <see cref="CallsController"/> or <see cref="FrontController"/>.
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
        private AvRow allocationRow;
        private readonly FrontCardPart[] cards = new FrontCardPart[FrontRules.FrontCount];
        private readonly Dictionary<SupportActionId, PerkRowPart> perkRows = new Dictionary<SupportActionId, PerkRowPart>(16);
        private OrderStrip strip;
        private CruisePart cruisePart;
        private AvSection[] groups;
        private static readonly string[] AheadWords = CountWords("", "/" + FrontRules.FrontCount + " FRONTS AHEAD", FrontRules.FrontCount);
        private static readonly string[] AlertWords = CountWords("", " ALERTS", 12);
        private static readonly string[] ReadyWords = CountWords("", " PERKS READY", 32);

        private static string[] CountWords(string prefix, string suffix, int max)
        {
            var words = new string[max + 1];
            for (int i = 0; i < words.Length; i++) words[i] = prefix + i + suffix;
            words[1] = words[1].Replace("ALERTS", "ALERT").Replace("PERKS", "PERK");
            return words;
        }

        private static string Count(string[] words, int n) => words[Math.Max(0, Math.Min(n, words.Length - 1))];
        private OpsTab tab = OpsTab.Fronts;

        // Local inspection state: which perk the strip shows. Inspecting never arms or spends.
        private SupportActionId? inspectedPerk, lastArmed;

        private float nextRefresh, failedUntil;
        private bool refreshFaulted;
        private Canvas pageCanvas;
        private int aimCellX = int.MinValue, aimCellZ;
        private string aimGrid = "";

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
            allocationRow = null;
            groups = null; strip = null; cruisePart = null;
            for (int i = 0; i < cards.Length; i++) cards[i] = null;
            perkRows.Clear();
            tab = OpsTab.Fronts;
            inspectedPerk = lastArmed = null;
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
                if (FavouriteOwns(KeyCode.Alpha0 + d) || FavouriteOwns(KeyCode.Keypad0 + d)) return; // one keypress, one action: a bound favourite wins, the tab does not switch
                if ((OpsTab)d != tab) console.SetPage(d - 1);
                return;
            }
        }

        /// <summary>True when a filled favourite slot is bound to exactly this bare key (CallsController polls it as a hotkey).</summary>
        private bool FavouriteOwns(KeyCode key)
        {
            for (int i = 0; i < calls.Favourites.Length; i++)
            {
                ConfigEntry<KeyboardShortcut> bound = FavouriteKey(i);
                if (bound != null && bound.Value.MainKey == key && !bound.Value.Modifiers.Any() && calls.Favourites[i].HasValue) return true;
            }
            return false;
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

        private void BuildConsole(RectTransform host, float height)
        {
            console = AvConsole.Build(host, "OPS", "SUPPORT", 2, Width, height);
            AvTabBar tabs = console.Tabs((AvIcon.Flag, "OPS FRONTS"), (AvIcon.Target, "PERKS"));
            string[] hints =
            {
                "[1] Ops fronts: status, blocker, your skill grade, superiority, your support, what is building. OPEN for its window.",
                "[2] Click a perk to arm it, aim (pod, map right-click or a team laser), then FIRE. The cruise block steers your salvo."
            };
            AvControl[] tabButtons = tabs.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabButtons.Length && i < hints.Length; i++) tabButtons[i].Help = hints[i];
            CountryStrip.Mount(console);
            console.PageChanged += OnPage;

            // FRONTS: the player's allocation (their own share; the strip above is the faction's), then three cards.
            AvFlow p0 = console.Page(0);
            allocationRow = p0.Add(new AvRow(p0.Content));
            allocationRow.Help = "ALLOCATION: your own share, spent on perks, programmes and posted strikes. AHEAD counts the fronts where your side holds superiority.";
            for (int i = 0; i < cards.Length; i++) cards[i] = p0.Add(new FrontCardPart(p0.Content, (Front)i, OpenFront));

            // PERKS: the flying cruise's block (only while one flies), then the perks grouped by front as call cards. The pinned strip sits over the top of the page.
            float stripSpace = OrderStrip.Height - AvGridTokens.Gap;
            AvFlow p1 = console.Page(1);
            p1.Space(stripSpace);
            cruisePart = p1.Add(new CruisePart(p1.Content, () => calls?.AddLeg(), () => calls?.CancelPick(), () => calls?.ClearRoute()));
            cruisePart.SetShown(false);
            groups = new AvSection[FrontRules.FrontCount];
            for (int f = 0; f < FrontRules.FrontCount; f++)
            {
                var front = (Front)f;
                groups[f] = p1.Section(f == 0 ? AvIcon.Satellite : f == 1 ? AvIcon.Antenna : AvIcon.UsersGroup, FrontRules.Name(front) + " PERKS");
                foreach (CallRow r in CallSheet.Rows)
                    if (r.Front == front) perkRows[r.Id] = p1.Add(new PerkRowPart(p1.Content, InspectPerk, r.Id));
            }

            console.Orders(0, "OPEN a front for its map, programmes and perks.");
            console.Orders(1, "Click a perk to arm it, aim (pod, laser or map), then FIRE. Nothing is spent until FIRE.");
            console.Finish();

            // The strip is a direct child of the console, after every page canvas, so it draws and takes clicks over scrolled content. It is
            // seated just under the tab bar: the body rect is page > viewport > body in the console's own hierarchy.
            strip = new OrderStrip(console.Root, StripPress, () => { calls?.Disarm(); nextRefresh = 0f; });
            console.Ticker.Register(strip);
            var bodyRect = (RectTransform)console.Page(0).Content.parent.parent;
            strip.Dock(AvGridTokens.Pad, console.Page(0).Inner, -bodyRect.anchoredPosition.y + 4f);
            console.SetPage(0);
            OnPage(0);
        }

        private void OpenFront(Front front)
        {
            fronts?.Open(front);
        }

        /// <summary>The strip's primary button: ARM prepares (and never fires), FIRE sends only the exact perk armed right now.</summary>
        private void StripPress(bool fire, int id)
        {
            if (calls == null) return;
            var perk = (SupportActionId)id;
            if (fire) calls.FireArmed(perk); else calls.Arm(perk);
            nextRefresh = 0f; // repaint on the next frame: the press feedback does not wait for the 0.2 s tick
        }

        /// <summary>A perk card click arms it (arming spends nothing; an armed perk stays armed, so a second click can never fire it). A refused arm still shows the perk and why in the strip.</summary>
        private void InspectPerk(SupportActionId id)
        {
            inspectedPerk = id;
            calls?.Arm(id);
            nextRefresh = 0f;
        }

        private ConfigEntry<KeyboardShortcut> FavouriteKey(int slot)
        {
            SupportSettings s = manager?.Settings;
            return slot == 0 ? s?.CallKey1 : slot == 1 ? s?.CallKey2 : slot == 2 ? s?.CallKey3 : s?.CallKey4;
        }

        private void OnPage(int page)
        {
            tab = (OpsTab)(page + 1);
            nextRefresh = 0f;
            if (strip != null) strip.Rect.gameObject.SetActive(tab != OpsTab.Fronts);
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

#if !PUBLIC_RELEASE
        /// <summary>Automation: what a perk card click does.</summary>
        internal void ClickPerk(SupportActionId id) => InspectPerk(id);
        /// <summary>Automation: the strip's primary button (ARM or FIRE, as painted).</summary>
        internal void PressStrip()
        {
            StripView sv = view.PerkStrip;
            if (sv.Has && sv.PrimaryEnabled) StripPress(sv.PrimaryFire, sv.Id);
        }
        /// <summary>Automation: the strip's DISARM button.</summary>
        internal void PressDisarm() { calls?.Disarm(); nextRefresh = 0f; }
#endif

        internal bool Installed => screen != null;
        internal bool ScreenActive => screen != null && screen.isActive;
        internal OpsTab Tab => tab;
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
                    manager.LocalCooldownRemaining(row.Id), calls.Armed == row.Id, calls.PendingAction == row.Id, !manager.Online));
            view.Allocation = (int)manager.LocalAllocation;
            FollowArm();
            OpsPageViews.Perks(view, tiles);
            FrontMirror fm = manager.FrontMirror;
            OpsPageViews.Cards(view, fm.State, fm.Known, now, manager.Online, manager.LocalGrades());
            OpsPageViews.Order(view, tiles, calls.AimNow, AimGridNow(), calls.Pending, calls.LastWords, inspectedPerk);
            view.CruiseFlying = manager.TryFlyingCruise(out int cruiseId, out view.CruiseLegs);
            view.CruiseRoute = view.CruiseFlying ? RouteLine(manager.CruiseLegs(cruiseId)) : "";
            view.Steering = calls.SteeringCruise;
            view.FrontWords = fronts != null && SupportManager.MissionNow() - fronts.LastWordsAt < 8f ? fronts.LastWords : "";
            view.FrontWordsTone = fronts != null ? fronts.LastWordsTone : AvState.Inert;
            Paint(view);
        }

        /// <summary>The strip follows the live order: a perk that just became armed (here or from a hotkey) is the one inspected.</summary>
        private void FollowArm()
        {
            SupportActionId? armed = calls.Armed;
            if (armed != lastArmed) { lastArmed = armed; if (armed.HasValue) inspectedPerk = armed; }
        }

        private string AimGridNow()
        {
            if (!calls.Armed.HasValue || !calls.TryAimPoint(out GlobalPosition p)) { aimCellX = int.MinValue; aimGrid = ""; return ""; }
            int cx = Mathf.RoundToInt((float)(p.x / 100.0)), cz = Mathf.RoundToInt((float)(p.z / 100.0));
            if (cx != aimCellX || cz != aimCellZ) { aimCellX = cx; aimCellZ = cz; aimGrid = OpsKit.Grid((float)p.x, (float)p.z); }
            return aimGrid;
        }

        /// <summary>Paints the open page (FRONTS' allocation line included), the pinned strip and the footer from <paramref name="v"/> (the harness feeds fixtures here).</summary>
        internal void Paint(OpsPageView v)
        {
            if (console == null) return;
            // Only the open page is painted; OnPage forces a refresh the moment another one is picked, so it repaints then.
            if (tab == OpsTab.Fronts)
            {
                allocationRow?.Set("ALLOCATION", Count(AheadWords, v.FrontsAhead), OpsPageViews.Ap(v.Allocation), v.Alerts > 0 ? AvState.Danger : v.Allocation > 0 ? AvState.Ready : AvState.Caution);
                for (int i = 0; i < cards.Length; i++) cards[i]?.Paint(v.Cards[i]);
            }

            if (tab == OpsTab.Perks)
            {
                bool cruiseOn = v.CruiseFlying || v.Steering; // the block exists only while there is a salvo to steer
                cruisePart.SetShown(cruiseOn);
                if (cruiseOn) cruisePart.Paint(v);
                foreach (PerkRowView p in v.Perks)
                    if (perkRows.TryGetValue(p.Id, out PerkRowPart row)) row.Paint(p);
                for (int f = 0; f < groups.Length; f++) groups[f].SetCaption(v.PowerLine[f]);
                strip?.Paint(v.PerkStrip.Has ? v.PerkStrip : OpsPageViews.Idle("WAITING FOR HOST", "NO PERKS READ YET", "", AvState.Inert));
            }

            string words; AvState tone;
            if (tab == OpsTab.Perks) { words = v.Words.Length > 0 ? v.Words : Count(ReadyWords, v.PerksReady); tone = v.Words.Length > 0 ? v.WordsTone : v.PerksReady > 0 ? AvState.Ready : AvState.Inert; }
            else if (v.FrontWords.Length > 0) { words = v.FrontWords; tone = v.FrontWordsTone; }
            else if (v.Alerts > 0) { words = Count(AlertWords, v.Alerts); tone = AvState.Danger; }
            else { words = "NO ALERTS"; tone = AvState.Ready; }
            console.Footer.Set(words, tone);
        }

        private static string RouteLine(IReadOnlyList<GlobalPosition> legs)
        {
            if (legs == null || legs.Count == 0) return "";
            var parts = new string[legs.Count];
            for (int i = 0; i < legs.Count; i++) parts[i] = OpsKit.Grid((float)legs[i].x, (float)legs[i].z);
            return string.Join(" › ", parts);
        }
    }
}
