using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
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
    /// <summary>The three tabs of the OPS bezel page.</summary>
    internal enum OpsTab : byte { Fronts = 1, Perks = 2, Tasked = 3 }

    /// <summary>
    /// The OPS bezel page, in the shared console frame every other MFD panel wears (<see cref="AvConsole"/>): header, four metrics, three icon
    /// tabs and the footer that carries the page's words and the hover help. FRONTS shows the three fronts as cards (status stamp, blocker,
    /// readiness, superiority, your support, programme phase) with an OPEN button each (the windows are <see cref="FrontController"/>'s);
    /// PERKS lists the pilot's perks by front as call cards (clicking one ARMS it: arming spends nothing) and TASKED the faction's posted strikes as
    /// package cards (clicking one inspects it). A strip pinned under the tabs (outside the scroller) shows the armed or inspected perk or package and
    /// carries ARM / FIRE / DISARM, so those never scroll away; the cruise block (ADD LEG, CANCEL PICK, CLEAR ROUTE) sits in its page. Keys 1-3
    /// switch tabs only while the pointer is over the page, and never a key a favourite is bound to. The panel owns no policy: every figure
    /// comes from <see cref="SupportManager"/> and every press goes through <see cref="CallsController"/> or <see cref="FrontController"/>.
    /// </summary>
    internal sealed class CallsPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.2f;
        private static readonly string[] PageTitles = { "FRONTS / FACTION STRATEGY", "PERKS / YOUR SUPPORT", "TASKED / FACTION STRIKE PACKAGES" };

        private readonly OpsPageView view = new OpsPageView();
        private readonly List<CallTile> tiles = new List<CallTile>(16);

        private SupportManager manager;
        private CallsController calls;
        private FrontController fronts;
        private SpaceFeedController feed;
        private ManualLogSource logger;

        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Ops, "OPS", "BoscaliOperations.Screen", preferLeft: true);
        private MFDScreen screen => installer.Screen;
        private AvConsole console;
        private AvMetric[] metrics;
        private readonly FrontCardPart[] cards = new FrontCardPart[FrontRules.FrontCount];
        private readonly Dictionary<SupportActionId, PerkRowPart> perkRows = new Dictionary<SupportActionId, PerkRowPart>(16);
        private OrderStrip strip;
        private CruisePart cruisePart;
        private AvSection[] groups;
        private AvSection taskedGroup;
        private readonly TaskedCardPart[] taskedRows = new TaskedCardPart[OpsPageView.MaxTaskedRows];
        private readonly int[] readyByFront = new int[FrontRules.FrontCount];
        private OpsTab tab = OpsTab.Fronts;

        // Local inspection state: which perk and which post the strip shows. Inspecting never arms, spends or claims.
        private SupportActionId? inspectedPerk, lastArmed;
        private int inspectedPost, lastArmedPost;

        private float nextRefresh, failedUntil;
        private bool refreshFaulted;
        private Canvas pageCanvas;
        private int aimCellX = int.MinValue, aimCellZ;
        private string aimGrid = "";

        public void Configure(SupportManager supportManager, CallsController callsController, FrontController frontController, SpaceFeedController feedController)
        {
            manager = supportManager;
            calls = callsController;
            fronts = frontController;
            feed = feedController;
            logger = installer.Log = ((ISupportHost)supportManager).Logger;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();
            console = null;
            metrics = null;
            groups = null; strip = null; cruisePart = null;
            taskedGroup = null;
            for (int i = 0; i < taskedRows.Length; i++) taskedRows[i] = null;
            if (feed != null) feed.PanelWatch = false;
            for (int i = 0; i < cards.Length; i++) cards[i] = null;
            perkRows.Clear();
            tab = OpsTab.Fronts;
            inspectedPerk = lastArmed = null;
            inspectedPost = lastArmedPost = 0;
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
            if (feed != null) feed.PanelWatch = visible && tab == OpsTab.Tasked; // the TASKED rows need the SPACE posts
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

        /// <summary>Digit keys 1-3 pick a tab, only with the pointer over the page (in flight the keys belong to the weapons), never while typing or with a modifier held.</summary>
        private void PollTabKeys()
        {
            if (!Input.anyKeyDown) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;
            if (GameplayUI.GameIsPaused || !Application.isFocused) return;
            if (!C2Tabs.KeyAllowed(PointerOverPage(), false, InputFieldChecker.InsideInputField)) return;
            for (int d = 1; d <= 3; d++)
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
            console = AvConsole.Build(host, "OPS", PageTitles[0], 3, Width, height);
            AvTabBar tabs = console.Tabs((AvIcon.Flag, "FRONTS"), (AvIcon.Target, "PERKS"), (AvIcon.Clipboard, "TASKED"));
            string[] hints =
            {
                "[1] Fronts: status, blocker, readiness, superiority, your support, what is building. OPEN for its window.",
                "[2] Click a perk to arm it, aim (pod, map right-click or a team laser), then FIRE.",
                "[3] Click a posted strike to arm it, then FIRE claims and launches it. The cruise block steers your salvo."
            };
            AvControl[] tabButtons = tabs.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabButtons.Length && i < hints.Length; i++) tabButtons[i].Help = hints[i];
            metrics = console.Metrics("YOUR ALLOCATION", "PERKS READY", "FRONTS AHEAD", "ALERTS");
            console.PageChanged += OnPage;

            // FRONTS: three cards. A short panel drops their secondary lines (programme, queue, directive, latest event) before anything else.
            float body = height - (AvGridTokens.Header + 4f + AvGridTokens.Metric + 6f + AvGridTokens.Tab + 4f + AvGridTokens.Footer);
            CardTier tier = body >= 540f ? CardTier.Full : body >= 360f ? CardTier.Mid : CardTier.Core;
            AvFlow p0 = console.Page(0);
            for (int i = 0; i < cards.Length; i++) cards[i] = p0.Add(new FrontCardPart(p0.Content, (Front)i, OpenFront, tier));

            // PERKS: the perks grouped by front as call cards. The pinned strip sits over the top of the page.
            float stripSpace = OrderStrip.Height - AvGridTokens.Gap;
            AvFlow p1 = console.Page(1);
            p1.Space(stripSpace);
            groups = new AvSection[FrontRules.FrontCount];
            for (int f = 0; f < FrontRules.FrontCount; f++)
            {
                var front = (Front)f;
                groups[f] = p1.Section(f == 0 ? AvIcon.Satellite : f == 1 ? AvIcon.Antenna : AvIcon.UsersGroup, FrontRules.Name(front) + " PERKS");
                foreach (CallRow r in CallSheet.Rows)
                    if (r.Front == front) perkRows[r.Id] = p1.Add(new PerkRowPart(p1.Content, InspectPerk, r.Id));
            }

            // TASKED: the flying cruise's block (only while one flies), then the posted strikes as package cards. Only real posts are drawn.
            AvFlow p2 = console.Page(2);
            p2.Space(stripSpace);
            cruisePart = p2.Add(new CruisePart(p2.Content, () => calls?.AddLeg(), () => calls?.CancelPick(), () => calls?.ClearRoute()));
            cruisePart.SetShown(false);
            taskedGroup = p2.Section(AvIcon.Clipboard, "POSTED PACKAGES", "");
            taskedGroup.SetShown(false);
            for (int i = 0; i < taskedRows.Length; i++)
            {
                taskedRows[i] = p2.Add(new TaskedCardPart(p2.Content, InspectPost, "Perk Tasked " + i));
                taskedRows[i].SetShown(false);
            }

            console.Orders(0,
                "OPEN a front for its map, programmes and perks.",
                "A stamp says OPERATIONAL, DEGRADED, OFFLINE or WAITING; the line under it names what is wrong now.",
                "Readiness and superiority belong to the faction and are shared by every pilot on your side.");
            console.Orders(1,
                "Click a perk to arm it. Nothing is spent until FIRE.",
                "Aim with your pod or a team laser, or right-click the map (that fires at once). Otherwise press FIRE.",
                "DISARM spends nothing. Keys: bind CallSlot 1-4 in the mod settings (Support Keys).");
            console.Orders(2,
                "Click a package to arm it (nothing is claimed yet); FIRE claims it and launches.",
                "ADD LEG, then right-click the map: one leg per press. CANCEL PICK keeps the route; CLEAR ROUTE drops it.");
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

        /// <summary>The strip's primary button: ARM prepares (and never fires), FIRE sends only the exact perk or post armed right now.</summary>
        private void StripPress(bool fire, int id)
        {
            if (calls == null) return;
            if (tab == OpsTab.Tasked)
            {
                if (fire) calls.FireTasked(id); else calls.ArmTasked(id);
            }
            else
            {
                var perk = (SupportActionId)id;
                if (fire) calls.FireArmed(perk); else calls.Arm(perk);
            }
            nextRefresh = 0f; // repaint on the next frame: the press feedback does not wait for the 0.2 s tick
        }

        /// <summary>A perk card click arms it (arming spends nothing; an armed perk stays armed, so a second click can never fire it). A refused arm still shows the perk and why in the strip.</summary>
        private void InspectPerk(SupportActionId id)
        {
            inspectedPerk = id;
            calls?.Arm(id);
            nextRefresh = 0f;
        }

        /// <summary>A package card click arms it (local only: nothing is claimed or spent until FIRE); a refused arm still shows the package and why.</summary>
        private void InspectPost(int postId)
        {
            inspectedPost = postId;
            calls?.ArmTasked(postId);
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
            console?.SetTitle(PageTitles[Mathf.Clamp(page, 0, PageTitles.Length - 1)]);
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
        /// <summary>Automation: what a package card click does.</summary>
        internal void ClickPost(int postId) => InspectPost(postId);
        /// <summary>Automation: the strip's primary button (ARM or FIRE, as painted) on the open tab.</summary>
        internal void PressStrip()
        {
            StripView sv = tab == OpsTab.Tasked ? view.PostStrip : view.PerkStrip;
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
            OpsPageViews.Cards(view, fm.State, fm.Known, now, manager.Online);
            OpsPageViews.Order(view, tiles, calls.AimNow, AimGridNow(), calls.Pending, calls.LastWords, inspectedPerk);
            SpaceFeedMirror sm = manager.SpaceMirror;
            if (!sm.Known) inspectedPost = 0; // post ids restart with each faction's desk and each mission: a pick must not outlive the mirror it was made on and land on another faction's post
            OpsPageViews.Tasked(view, sm.State.Posts, sm.Known, now, calls.TaskedArmedId, calls.TaskedClaimPending, inspectedPost);
            if (view.PostGone) inspectedPost = OpsPageViews.PostGone; // the strip keeps saying so; nothing else is selected in its place
            view.CruiseFlying = manager.TryFlyingCruise(out int cruiseId, out view.CruiseLegs);
            view.CruiseRoute = view.CruiseFlying ? RouteLine(manager.CruiseLegs(cruiseId)) : "";
            view.Steering = calls.SteeringCruise;
            view.FrontWords = fronts != null && SupportManager.MissionNow() - fronts.LastWordsAt < 8f ? fronts.LastWords : "";
            view.FrontWordsTone = fronts != null ? fronts.LastWordsTone : AvState.Inert;
            Paint(view);
        }

        /// <summary>The strip follows the live order: a perk or post that just became armed (here or from a hotkey) is the one inspected.</summary>
        private void FollowArm()
        {
            SupportActionId? armed = calls.Armed;
            if (armed != lastArmed) { lastArmed = armed; if (armed.HasValue) inspectedPerk = armed; }
            int post = calls.TaskedArmedId;
            if (post != lastArmedPost) { lastArmedPost = post; if (post != 0) inspectedPost = post; }
        }

        private string AimGridNow()
        {
            if (!calls.Armed.HasValue || !calls.TryAimPoint(out GlobalPosition p)) { aimCellX = int.MinValue; aimGrid = ""; return ""; }
            int cx = Mathf.RoundToInt((float)(p.x / 100.0)), cz = Mathf.RoundToInt((float)(p.z / 100.0));
            if (cx != aimCellX || cz != aimCellZ) { aimCellX = cx; aimCellZ = cz; aimGrid = OpsKit.Grid((float)p.x, (float)p.z); }
            return aimGrid;
        }

        /// <summary>Paints the metrics, the open page, the pinned strip and the footer from <paramref name="v"/> (the harness feeds fixtures here).</summary>
        internal void Paint(OpsPageView v)
        {
            if (console == null) return;
            metrics[0].Set(v.Allocation.ToString(), "AP", Mathf.Clamp01(v.Allocation / 100f), v.Allocation > 0 ? AvState.Ready : AvState.Caution);
            metrics[1].Set(v.PerksReady + "/" + v.PerksTotal, "READY", v.PerksTotal > 0 ? v.PerksReady / (float)v.PerksTotal : 0f, v.PerksReady > 0 ? AvState.Ready : AvState.Caution);
            metrics[2].Set(v.FrontsAhead + "/" + FrontRules.FrontCount, "AHEAD", v.FrontsAhead / (float)FrontRules.FrontCount, v.FrontsAhead > 0 ? AvState.Ready : AvState.Caution);
            metrics[3].Set(v.Alerts.ToString(), v.Alerts == 0 ? "CLEAR" : "ACTIVE", Mathf.Clamp01(v.Alerts / 3f), v.Alerts > 0 ? AvState.Danger : AvState.Ready);

            // Only the open page is painted; OnPage forces a refresh the moment another one is picked, so it repaints then.
            if (tab == OpsTab.Fronts)
                for (int i = 0; i < cards.Length; i++) cards[i]?.Paint(v.Cards[i]);

            if (tab == OpsTab.Perks)
            {
                Array.Clear(readyByFront, 0, readyByFront.Length);
                foreach (PerkRowView p in v.Perks)
                {
                    if (perkRows.TryGetValue(p.Id, out PerkRowPart row)) row.Paint(p);
                    if (p.Enabled && !p.Armed && !p.Pending) readyByFront[(int)p.Front]++;
                }
                for (int f = 0; f < groups.Length; f++) groups[f].SetCaption((v.PowerLine[f].Length > 0 ? v.PowerLine[f] + " · " : "") + readyByFront[f] + " READY");
                strip?.Paint(v.PerkStrip.Has ? v.PerkStrip : OpsPageViews.Idle("WAITING FOR HOST", "NO PERKS READ YET", "", AvState.Inert));
            }
            if (tab == OpsTab.Tasked)
            {
                PaintTasked(v);
                strip?.Paint(v.PostStrip.Has ? v.PostStrip : OpsPageViews.Idle("WAITING FOR HOST", "THE SPACE FEED HAS NOT ANSWERED YET", "", AvState.Inert));
            }

            string words; AvState tone;
            if (tab == OpsTab.Perks) { words = v.Words.Length > 0 ? v.Words : "CLICK A PERK TO ARM IT, AIM, THEN FIRE"; tone = v.WordsTone; }
            else if (tab == OpsTab.Tasked) { words = v.Words.Length > 0 ? v.Words : "CLICK A POSTED STRIKE TO ARM IT, THEN FIRE"; tone = v.WordsTone; }
            else if (v.FrontWords.Length > 0) { words = v.FrontWords; tone = v.FrontWordsTone; }
            else { words = "OPEN A FRONT FOR ITS MAP, PROGRAMMES AND PERKS"; tone = AvState.Ready; }
            console.Footer.Set(words, tone);
        }

        private static string RouteLine(IReadOnlyList<GlobalPosition> legs)
        {
            if (legs == null || legs.Count == 0) return "";
            var parts = new string[legs.Count];
            for (int i = 0; i < legs.Count; i++) parts[i] = OpsKit.Grid((float)legs[i].x, (float)legs[i].z);
            return string.Join(" › ", parts);
        }

        private void PaintTasked(OpsPageView v)
        {
            bool cruiseOn = v.CruiseFlying || v.Steering; // the block exists only while there is a salvo to steer
            cruisePart.SetShown(cruiseOn);
            if (cruiseOn) cruisePart.Paint(v);
            taskedGroup.SetShown(v.Tasked.Count > 0);
            taskedGroup.SetCaption(v.TaskedTotal > v.Tasked.Count ? v.Tasked.Count + " OF " + v.TaskedTotal + " OPEN" : v.TaskedTotal + " OPEN");
            for (int i = 0; i < taskedRows.Length; i++)
            {
                bool has = i < v.Tasked.Count; // only real posts are drawn: an empty slot is hidden, not painted as a dead row
                taskedRows[i].SetShown(has);
                if (has) taskedRows[i].Paint(v.Tasked[i]);
            }
        }
    }
}
