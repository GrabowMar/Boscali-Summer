using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Spec bezel v2 §3: the WMC bezel panel on the maximized map — TACTICAL · BEHAVIOUR ‖ SUPPLY · LOADOUT · WING (the
    /// user cut seven tabs to five on 2026-09-28: FORM and PLAN are BEHAVIOUR's sub-pages, INSPECT is WING's) on a kit v2
    /// <see cref="AvConsole"/> (display glass included), claimed through <see cref="BezelRegistry.Wmc"/>, refreshed at
    /// 5 Hz. The header is the wing's title, a FUNDS chip beside it, and three vitals chips (FUEL MIN · AMMO MIN · THREAT) on every
    /// tab; there are no metric tiles. Phase B: every tab is an <see cref="IWmcPage"/> that builds into its console page's
    /// <see cref="AvFlow"/> .
    /// Control ids answer through <see cref="WmcControls"/>.</summary>
    internal sealed class WmcPanel : IWingService
    {
        public static WmcPanel Instance { get; private set; }
        public string Name => "WMC";

        private readonly WmcControls controls = new WmcControls();
        private readonly WmcContext context = new WmcContext();
        private readonly WmcMapOverlay overlay = new WmcMapOverlay();
        private readonly int[] headerKeys = { -1, -1, -1, -1, -1 };
        private IWmcPage[] pages;
        private WmcTactical tactical;
        private WmcPlan plan;
        private WmcSupply supply;
        private WmcLoadout loadout;
        private WmcWing wingPage;
        private int shownPage = -1;
        private readonly HashSet<int> pageFaults = new HashSet<int>();
        private MFDScreen screen;
        private Button bezelButton;
        private GameObject root;
        private RectTransform content;
        private AvConsole shell;
        private AvChip[] chips;
        private AvChip fundsChip;
        private RectTransform[] hosts;
        private Rect bodyRect;
        private float nextAttempt, nextRefresh;
        private bool wasVisible;
        private bool gaveUp;

        public WmcPanel()
        {
            Instance = this;
            context.Map.Placed += overlay.Ping;
        }

        public bool Visible => screen != null && screen.isActive && DynamicMap.mapMaximized;
        /// <summary>A page with a scope row that gives orders is on show (spec bezel v2 §6: an unarmed right-click MOVE is TACTICAL's,
        /// FORM's and OPTIONS' only; ROUTE's right-click draws).</summary>
        public bool CommandShowing => Visible && shell != null
            && ((shell.CurrentPage == WmcTabs.Tactical && tactical != null && tactical.Sub != WmcTactical.SubRoute)
                || (shell.CurrentPage == WmcTabs.Behaviour && plan != null && plan.Sub == WmcPlan.SubOptions));
        /// <summary>BEHAVIOUR › PLAN is showing: its map tools stay armed only here.</summary>
        public bool PlanShowing => Visible && shell != null && shell.CurrentPage == WmcTabs.Behaviour && plan != null && plan.Sub == WmcPlan.SubPlan;
        public int Page => shell != null ? shell.CurrentPage : -1;
        public string PageName => shell != null && shell.CurrentPage >= 0 && shell.CurrentPage < WmcTabs.Labels.Length ? WmcTabs.Labels[shell.CurrentPage] : "";
        public WmcControls Controls => controls;
        public WmcContext Context => context;
        public WmcMapOverlay Overlay => overlay;
        public WmcTactical Tactical => tactical;
        public WmcForm Form => tactical?.Form;
        public WmcRoute Route => tactical?.Route;
        public WmcPlan Plan => plan;
        public WmcInspect InspectPage => wingPage?.InspectPage;
        public WmcSupply Supply => supply;
        public WmcLoadout Loadout => loadout;
        public WmcWing WingPage => wingPage;
        /// <summary>Labels that would still spill out of their box (the automation's text-fit audit).</summary>
        public int Overflow => OnShow() != null ? WmcKit.Overflow(OnShow()) : 0;

        /// <summary>The page host on show (hidden pages keep their objects active, so audits must not look at them).</summary>
        private RectTransform OnShow() => hosts != null && shell != null && shell.CurrentPage >= 0 && shell.CurrentPage < hosts.Length
            ? hosts[shell.CurrentPage] : null;

        /// <summary>The tallest empty band on the page on show, in px (the automation's density audit).</summary>
        public int Gap => OnShow() != null ? WmcKit.LargestGap(OnShow(), bodyRect) : 0;

        public void Activate()
        {
            Reset();
            DynamicMap.onMapChanged -= overlay.Dirty;
            DynamicMap.onMapChanged += overlay.Dirty;
        }

        public void Deactivate()
        {
            DynamicMap.onMapChanged -= overlay.Dirty;
            Reset();
        }

        public void FixedTick(float dt)
        {
        }

        public void Tick(float dt)
        {
            // Map and HUD marks do not need the panel (spec WMC program §5); they ride on its tick.
            WingMarkers.Tick(WingService.Instance);
            // R5: a text field lets go after Enter, and never keeps the keyboard once the panel is out of sight.
            WmcNameField.TickAll();
            bool visible = Visible;
            if (wasVisible && !visible) WmcNameField.BlurAny();
            wasVisible = visible;
            bool enabled = !gaveUp && WingSettings.Instance.ShowWmc.Value && GameAccess.MfdAvailable;
            if (!enabled)
            {
                if (screen != null && screen.isActive) screen.CloseScreen(screen.transform.localPosition);
                context.Map.Update(context, false);
                overlay.Hide();
                return;
            }
            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }
            MfdPresentation.Tick();
            // Every frame: the right button is followed per frame (spec WMC program §5).
            context.Map.Update(context, Visible);
            overlay.Tick(context, Visible);
            if (!Visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + WingFidelity.Interval(0.2f);
            Refresh();
        }

        /// <summary>Maximize the map and select the WMC screen, as the player's bezel press would (automation).</summary>
        public void Open()
        {
            if (gaveUp)
            {
                WingToast.Show("The WMC could not install on the map's bezel (see the log); orders stay on the radial menu");
                return;
            }
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            if (screen != null && !screen.isActive && bezelButton != null) bezelButton.onClick.Invoke();
            nextRefresh = 0f;
        }

        /// <summary>Latch a tab (automation; the tab bar calls <see cref="AvConsole.SetPage"/> itself). A tab not built yet stays
        /// where it is.</summary>
        public void Show(int tab)
        {
            if (shell == null || tab < 0 || tab >= WmcTabs.Labels.Length || pages[tab] == null) return;
            shell.SetPage(tab);
            nextRefresh = 0f;
        }

        /// <summary>INSPECT on this aircraft (INSPECT › on a row, a log line, automation); it never changes who orders go to.</summary>
        public void Inspect(uint id)
        {
            if (wingPage == null || id == 0u) return;
            wingPage.InspectPage.Focus(id);
            Show(WmcTabs.Wing);
            wingPage.ShowSubNamed("INSPECT");
            Refresh();
        }

        /// <summary>Press a control by id as a click would (automation). A page's control shows its page first. False when there
        /// is none or it is hidden; a disabled button ignores the click itself.</summary>
        public bool Press(string id)
        {
            int tab = WmcTabs.Of(id);
            if (tab >= 0 && pages != null && pages[tab] != null)
            {
                Show(tab);
                if (tab == WmcTabs.Tactical) tactical.ShowSubFor(id);
                if (tab == WmcTabs.Behaviour) plan.ShowSubFor(id);
                if (tab == WmcTabs.Squadron) wingPage.ShowSubFor(id);
                Refresh();
            }
            // Kit v2 chrome (tab bar, FIT) is not in the control registry: press it by id here.
            if (id != null && id.StartsWith("tab.", StringComparison.Ordinal))
            {
                int t = WmcTabs.Index(id.Substring(4));
                if (t < 0 || pages == null || pages[t] == null) return false;
                Show(t);
                Refresh();
                return true;
            }
            if (id == "hdr.fit")
            {
                if (fitButton == null || !fitButton.gameObject.activeInHierarchy) return false;
                PressFit();
                nextRefresh = 0f;
                return true;
            }
            if (!controls.Press(id)) return false;
            nextRefresh = 0f;
            return true;
        }

        private void Reset()
        {
            WmcNameField.BlurAny();
            MfdPresentation.Reset();
            BezelRegistry.Release(BezelRegistry.Wmc);
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            content = null;
            screen = null;
            bezelButton = null;
            shell = null;
            chips = null;
            fundsChip = null;
            hosts = null;
            pages = null;
            tactical = null;
            plan = null;
            supply = null;
            loadout = null;
            wingPage = null;
            shownPage = -1;
            fitButton = null;
            fitStep = -1;
            for (int i = 0; i < headerKeys.Length; i++) headerKeys[i] = -1;
            controls.Clear();
            context.Selection.Clear();
            context.Map.Disarm();
            PauseKeyHold.ReleaseAll();
            context.Draft.Clear();
            overlay.Destroy();
            WingMarkers.Reset();
            nextAttempt = nextRefresh = 0f;
            gaveUp = false;
        }

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;
                RetireStaleScreens();
                if (!MfdBezel.TryClaim(preferLeft: true, mfd: mfd,
                        out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    Fail("no free bezel button on either column");
                    return;
                }
                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    BezelRegistry.Release(BezelRegistry.Wmc);
                    return;
                }
                screen = Build(template, buttons[slot], out float height);
                if (screen == null)
                {
                    BezelRegistry.Release(BezelRegistry.Wmc);
                    return;
                }
                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    // Another plugin took the slot in the same frame: retry next second.
                    Reset();
                    return;
                }
                bezelButton = buttons[slot];
                MfdPresentation.Register(screen, screen.displayPanel.transform as RectTransform,
                    new Vector2(AvTokens.PanelWidth, height), buttons[slot], left);
                WingLog.Verbose("[WMC] installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1));
            }
            catch (Exception e)
            {
                Fail(e.Message);
                WingLog.Logger.LogError("[WMC] install failed: " + e);
            }
        }

        /// <summary>A hot reload leaves the previous WMC object in the slot; drop it so this one can claim the bezel.</summary>
        private static void RetireStaleScreens()
        {
            foreach (MFDScreen s in UnityEngine.Object.FindObjectsOfType<MFDScreen>())
                if (s != null && s.shortName == "WMC") UnityEngine.Object.Destroy(s.gameObject);
            BezelRegistry.Release(BezelRegistry.Wmc);
        }

        private void Fail(string reason)
        {
            gaveUp = true;
            BezelRegistry.Release(BezelRegistry.Wmc);
            screen = null;
            WingLog.Logger.LogWarning("[WMC] could not install the panel (" + reason + "). The radial menu still gives the orders; the WMC hotkey says why it cannot open.");
        }

        private static readonly AvIcon[] TabIcons =
            { AvIcon.Target, AvIcon.AdjustmentsHorizontal, AvIcon.Coins, AvIcon.Stack2, AvIcon.UsersGroup };

        private MFDScreen Build(MFDScreen template, Button bezel, out float height)
        {
            root = new GameObject("Wmc", typeof(RectTransform), typeof(Image));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(template.transform.parent, false);
            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;
            height = ResolveHeight(templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);

            // The console draws its own frame; this invisible plate only keeps clicks off the map beneath.
            Image plate = root.GetComponent<Image>();
            plate.color = Color.clear;
            plate.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            content = (RectTransform)contentObject.transform;
            content.SetParent(rootRect, false);
            AvLay.Fill(content);

            // Spec bezel v2 §3: no metric row; the chips carry the vitals on every tab.
            shell = AvConsole.Build(content, "WMC", "WING", WmcTabs.Labels.Length, AvTokens.PanelWidth, height);
            chips = shell.Chips(3);
            var items = new (AvIcon icon, string label)[WmcTabs.Labels.Length];
            for (int i = 0; i < items.Length; i++) items[i] = (TabIcons[i], WmcTabs.Labels[i]);
            shell.Tabs(items);
            fundsChip = new AvChip(shell.Root);
            shell.Ticker.Register(fundsChip);
            // Left of the console serial (width - 116, 54 wide): the chip used to cover it.
            AvLay.Place(fundsChip.Rect, AvTokens.PanelWidth - 116f - 4f - 114f, 4f, 114f, AvGridTokens.ChipStrip);
            fundsChip.Set("FUNDS " + WmcHeader.None, AvState.Inert);
            WmcMotion.Attach(root);
            BuildFitButton();

            // Body: what the console leaves between the tab bar and the footer (Layout: header + chips + tabs, then footer).
            float top = AvGridTokens.Header + 4f + AvGridTokens.ChipStrip + 6f + AvGridTokens.Tab + 4f;
            bodyRect = new Rect(0f, 0f, AvTokens.PanelWidth, Mathf.Max(40f, height - top - AvGridTokens.Footer));

            // TACTICAL is a kit v2 flow page. The rest are LEGACY (phase B2 converts them): their Rect-based build is hosted in one
            // fixed-height part of their flow, and their v1 buttons register in controls.Legacy.
            tactical = new WmcTactical(controls);
            plan = new WmcPlan(controls);
            supply = new WmcSupply(controls);
            loadout = new WmcLoadout(controls);
            wingPage = new WmcWing(controls);
            pages = new IWmcPage[]
            {
                tactical, plan, supply,
                loadout, wingPage,
            };
            hosts = new RectTransform[pages.Length];
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] == null) continue;
                AvFlow page = shell.Page(i);
                hosts[i] = page.Content;
                pages[i].Build(page, shell.Ticker, i);
            }
            shell.PageChanged += OnPage;
            shell.Finish();

            MFDScreen s = root.AddComponent<MFDScreen>();
            s.shortName = "WMC";
            s.displayPanel = contentObject;
            s.aircraftOnly = false;
            s.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            s.highlight = FindHighlight(bezel);
            if (s.label == null || s.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
                return null;
            }
            AvDisplayGlass.Attach(content);
            shell.SetPage(WmcTabs.Tactical);
            return s;
        }

        /// <summary>The height this panel takes in the slot it was parented into (the template's bay, measured), clamped.</summary>
        private static float ResolveHeight(RectTransform parent, float min, float max)
        {
            if (max < min) max = min;
            if (parent == null) return min;
            float available = parent.rect.height;
            RectTransform cursor = parent;
            for (int i = 0; i < 4 && available <= 1f && cursor != null; i++)
            {
                cursor = cursor.parent as RectTransform;
                if (cursor != null) available = cursor.rect.height;
            }
            return available <= 1f ? min : Mathf.Clamp(Mathf.Floor(available), min, max);
        }

        private static readonly string[] FitLabels = { "FIT", "ALL", "FOLLOW" };
        private AvControl fitButton;
        private int fitStep = -1;
        private float fitAt;

        /// <summary>[FIT] where the page index sat (spec bezel v2 §3): each press one step of FIT › ALL › FOLLOW; the label names the
        /// next step, and reads FIT again once the map follows you. Hidden when the game's map view could not be reached.</summary>
        private void BuildFitButton()
        {
            fitButton = AvControl.Make(shell.Root, new AvControl.Spec("FIT", PressFit, AvButtonStyle.Quiet));
            AvLay.Place(fitButton.Rect, AvTokens.PanelWidth - 60f, 3f, 56f, AvGridTokens.Header - 6f);
            fitButton.gameObject.SetActive(WmcMap.Usable);
            SetFit(0);
        }

        private void PressFit()
        {
            fitAt = Time.unscaledTime;
            switch (fitStep)
            {
                case 0:
                    var box = new MapBox();
                    Aircraft player = context.Wing?.Player;
                    if (player != null) box.Add(player.GlobalPosition().x, player.GlobalPosition().z);
                    for (int i = 0; i < context.Count; i++)
                    {
                        Unit u = WmcContext.UnitOf(context.Rows[i].Id);
                        if (u == null) continue;
                        GlobalPosition g = u.GlobalPosition();
                        box.Add(g.x, g.z);
                    }
                    WmcMap.Fit(box);
                    SetFit(1);
                    break;
                case 1:
                    WmcMap.All();
                    SetFit(2);
                    break;
                default:
                    WmcMap.Follow();
                    SetFit(0);
                    break;
            }
        }

        private void SetFit(int step)
        {
            if (fitButton == null || step == fitStep) return;
            fitStep = step;
            fitButton.Label = FitLabels[step];
        }

        private void OnPage(int tab)
        {
            WmcNameField.BlurAny();
            AvPopup.CloseAny();
            nextRefresh = 0f;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            foreach (Image img in button.GetComponentsInChildren<Image>(true))
                if (img.gameObject != button.gameObject) return img;
            return button.GetComponent<Image>();
        }

        /// <summary>The context's rows and scope now, bezel or not (the room reads it).</summary>
        public void FillContext() => Fill();

        private void Fill()
        {
            WingService wing = WingService.Instance;
            context.Wing = wing;
            // A client's wing is the host's: its rows arrive as the host's snapshot (spec M6 §8). The role decides, not the
            // aircraft: a client that has not spawned has none (review M7b-1 I3).
            context.Client = WingNet.ClientOnly;
            context.MissionTime = wing?.MissionTime ?? 0f;
            if (!context.Client)
            {
                context.Count = wing != null ? wing.FillSnapshot(context.Rows) : 0;
                context.Stale = false;
            }
            else
            {
                WingMirror mirror = WingNet.Mirror;
                context.Stale = mirror == null || mirror.Stale(Time.unscaledTime);
                context.Count = 0;
                if (mirror != null)
                    for (int i = 0; i < mirror.Count && i < context.Rows.Length; i++) context.Rows[context.Count++] = mirror[i];
            }
            // Review focus 1: a selected aircraft that left drops out; the scope follows the selection.
            context.Selection.Prune(context.Rows, context.Count);
            context.Rescope();
        }

        /// <summary>One refresh now (automation: a selection made this call reaches the scope before a press).</summary>
        public void Refresh()
        {
            if (shell == null || pages == null) return;
            Fill();
            RefreshHeader();
            int page = shell.CurrentPage;
            IWmcPage p = page >= 0 && page < pages.Length ? pages[page] : null;
            if (p != null)
            {
                // 1.0: a page that fails to refresh is logged once and skipped; it never takes the whole panel down.
                try
                {
                    if (page != shownPage) p.Shown(context);
                    p.Refresh(context);
                }
                catch (Exception e)
                {
                    if (pageFaults.Add(page)) WingLog.Logger.LogError("[WMC] the " + WmcTabs.Labels[page] + " page failed to refresh: " + e);
                }
            }
            shownPage = page;
            bool fresh = WingToast.Last != null && Time.unscaledTime - WingToast.LastAt < 6f;
            WriteStatus(p?.Alert, context.Map.Prompt(context.ScopeLabel), fresh ? WingToast.Last : p?.Hint ?? "");
        }

        /// <summary>Title and chips, each rebuilt only when its inputs change (no strings per refresh in steady state).</summary>
        private void RefreshHeader()
        {
            // The map follows you again (the game's own controls, or FOLLOW): FIT starts over.
            if (fitStep > 0 && Time.unscaledTime - fitAt > 1f && WmcMap.Following) SetFit(0);
            int airborne = 0;
            for (int i = 0; i < context.Count; i++)
            {
                var duty = (MemberDuty)context.Rows[i].Duty;
                if (duty != MemberDuty.Grounded && duty != MemberDuty.Settled) airborne++;
            }
            int max = WingService.MaxMembers;
            int pending = !context.Client && SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0;
            if (Changed(0, context.Count * 100000 + max * 1000 + airborne * 100 + pending * 4 + (context.Client ? 2 : 0) + (context.Stale ? 1 : 0)))
                shell.SetTitle(WmcHeader.Title(context.Count, max, airborne, pending, context.Client, context.Stale, out string _));
            WingVitals v = WingVitals.Of(context.Rows, context.Count);
            int fuelKey = v.Count * 100000 + Percent(v.MinFuel) * 100 + (v.BingoSlot + 1) * 10 + v.JokerSlot + 1;
            if (Changed(1, fuelKey)) Chip(0, WmcHeader.Fuel(v, out string s0), s0);
            int ammoKey = v.Count * 100000 + Percent(v.MinAmmo) * 100 + v.WinchesterSlot + 1;
            if (Changed(2, ammoKey)) Chip(1, WmcHeader.Ammo(v, out string s1), s1);
            int hostiles = context.Wing != null && !context.Client ? context.Wing.HostilesNear() : -1;
            if (Changed(3, (hostiles + 1) * 100 + v.DefendingSlot + 1)) Chip(2, WmcHeader.Threat(v, hostiles, out string s2), s2);
            // Spec FUI §header: FUNDS · FUEL · AMMO · THREAT (0.9's metrics as chips); the armed order is ORDERS' cue line.
            GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player);
            float funds = player != null && !context.Client ? player.Allocation : float.NaN;
            bool sandbox = WingSettings.Instance.SandboxFreeCalls.Value;
            if (Changed(4, (float.IsNaN(funds) ? -1 : (int)funds) * 4 + (sandbox ? 2 : 0) + (context.Client ? 1 : 0)))
                fundsChip?.Set(WmcHeader.Funds(funds, sandbox, context.Client, out string s3), StateOf(s3));
        }

        private static int Percent(float f) => float.IsNaN(f) ? -1 : Mathf.RoundToInt(f * 100f);

        private bool Changed(int slot, int key)
        {
            if (headerKeys[slot] == key) return false;
            headerKeys[slot] = key;
            return true;
        }

        private void Chip(int i, string text, string state)
        {
            if (chips != null && i < chips.Length) chips[i].Set(text, StateOf(state));
        }

        /// <summary>WmcHeader's state classes (live, warn, danger, info, inert) as kit v2 states; the chip adds the glyph word.</summary>
        private static AvState StateOf(string state)
        {
            switch (state)
            {
                case "live": return AvState.Ready;
                case "warn": return AvState.Caution;
                case "danger": return AvState.Danger;
                case "info": return AvState.Info;
                default: return AvState.Inert;
            }
        }

        /// <summary>The footer line: the armed-order prompt first, then an alert, else the page's ambient hint. Hover help replaces it
        /// while a control is hovered: kit v2 controls publish theirs to the console footer themselves (<see cref="AvHelpTip"/>).</summary>
        private void WriteStatus(string alert, string prompt, string ambient)
        {
            if (!string.IsNullOrEmpty(prompt)) shell.Footer.Set("ACTION  ·  " + prompt, AvState.Caution);
            else if (!string.IsNullOrEmpty(alert)) shell.Footer.Set("ALERT  ·  " + alert, AvState.Danger);
            else shell.Footer.Set("STATUS  ·  " + (ambient ?? ""), AvState.Inert);
        }
    }
}
