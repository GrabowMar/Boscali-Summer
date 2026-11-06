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
using CoreGameAccess = BoscaliSummer.Core.Game.GameAccess;
using MfdPanelInstaller = BoscaliSummer.Core.Game.MfdPanelInstaller;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Spec bezel v2 §3: the WMC bezel panel on the maximized map — TACTICAL · BEHAVIOUR ‖ SUPPLY · LOADOUT · WING (the
    /// user cut seven tabs to five on 2026-09-28: FORM and PLAN are BEHAVIOUR's sub-pages, INSPECT is WING's) on a kit v2
    /// <see cref="AvConsole"/> (display glass included), claimed through <see cref="BezelRegistry.Wmc"/>, refreshed at
    /// 5 Hz. The header is the wing's title, a FUNDS chip beside it, and three vitals chips (FUEL MIN · AMMO MIN · THREAT) on every
    /// tab; there are no metric tiles. Every tab is an <see cref="IWmcPage"/> that builds into its console page's
    /// <see cref="AvFlow"/>.</summary>
    internal sealed class WmcPanel : IWingService
    {
        public static WmcPanel Instance { get; private set; }
        public string Name => "WMC";

        private readonly WmcContext context = new WmcContext();
        private readonly WmcMapOverlay overlay = new WmcMapOverlay();
        private readonly int[] headerKeys = { -1, -1, -1, -1, -1 };
        private IWmcPage[] pages;
        private WmcTactical tactical;
        private WmcBehaviour behaviour;
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
                || (shell.CurrentPage == WmcTabs.Behaviour && behaviour != null));
        /// <summary>TACTICAL › ORDERS is showing: the QUEUE toggle stays on only here.</summary>
        public bool OrdersShowing => Visible && shell != null && shell.CurrentPage == WmcTabs.Tactical && tactical != null && tactical.Sub == WmcTactical.SubOrders;
        public WmcContext Context => context;
        public WmcMapOverlay Overlay => overlay;
        public WmcTactical Tactical => tactical;
        public WmcWing WingPage => wingPage;

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
            if (wasVisible && !visible)
            {
                WmcNameField.BlurAny();
                tactical?.CloseWindows();
            }
            wasVisible = visible;
            bool enabled = !gaveUp && WingSettings.Instance.ShowWmc.Value && CoreGameAccess.MfdAvailable;
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

        private void Reset()
        {
            WmcNameField.BlurAny();
            tactical?.CloseWindows(true);
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
            behaviour = null;
            supply = null;
            loadout = null;
            wingPage = null;
            shownPage = -1;
            shownGuide = null;
            pageFaults.Clear();
            wasVisible = false;
            statusAlert = statusPrompt = statusAmbient = null;
            fitButton = null;
            fitStep = -1;
            for (int i = 0; i < headerKeys.Length; i++) headerKeys[i] = -1;
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
                VirtualMFD mfd = MfdPanelInstaller.ResolveMfd();
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
                screen = Build(template, buttons[slot], out _);
                if (screen == null)
                {
                    Fail("the bezel button has no label or highlight");
                    return;
                }
                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    // Another plugin took the slot in the same frame: retry next second.
                    Reset();
                    return;
                }
                bezelButton = buttons[slot];
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
            Reset(); // drops the half-built root (its click plate would stay on screen) and the bezel claim
            gaveUp = true;
            WingLog.Logger.LogWarning("[WMC] could not install the panel (" + reason + "). The radial menu still gives the orders; the WMC hotkey says why it cannot open.");
        }

        private static readonly AvIcon[] TabIcons =
            { AvIcon.Target, AvIcon.AdjustmentsHorizontal, AvIcon.Coins, AvIcon.Stack2, AvIcon.UsersGroup };

        private MFDScreen Build(MFDScreen template, Button bezel, out float height)
        {
            // Before anything is built: a bezel without them can never take the screen.
            TextMeshProUGUI label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            Image highlight = MfdPanelInstaller.FindHighlight(bezel);
            height = 0f;
            if (label == null || highlight == null) return null;

            // The console draws its own frame; the invisible plate only keeps clicks off the map beneath.
            RectTransform rootRect = MfdPanelInstaller.MakeRoot(template, "Wmc", false, true, out root, out height);

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

            // Every page is a kit v2 flow page: it builds into the shell's flow for its tab.
            tactical = new WmcTactical();
            behaviour = new WmcBehaviour();
            supply = new WmcSupply();
            loadout = new WmcLoadout();
            wingPage = new WmcWing();
            pages = new IWmcPage[]
            {
                tactical, behaviour, supply,
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
            s.label = label;
            s.highlight = highlight;
            AvDisplayGlass.Attach(content);
            shell.SetPage(WmcTabs.Tactical);
            return s;
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
            tactical?.CloseWindows();
            nextRefresh = 0f;
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
            string[] guide = GuideFor(p);
            if (!ReferenceEquals(guide, shownGuide)) { shell.Orders(page, guide); shownGuide = guide; }
            bool fresh = WingToast.Last != null && Time.unscaledTime - WingToast.LastAt < 6f;
            WriteStatus(p?.Alert, context.Map.Prompt(context.ScopeLabel), fresh ? WingToast.Last : p?.Hint ?? "");
        }

        private string[] shownGuide;

        /// <summary>The footer's how-to lines (standing orders) for the sub-page on screen (<see cref="GuideWords"/>).</summary>
        private static string[] GuideFor(IWmcPage p)
        {
            switch (p)
            {
                case WmcTactical t: return t.Sub == WmcTactical.SubFormation ? GuideWords.Formation : t.Sub == WmcTactical.SubRoute ? GuideWords.Route : GuideWords.Orders;
                case WmcBehaviour _: return GuideWords.Behaviour;
                case WmcSupply _: return GuideWords.Supply;
                case WmcLoadout _: return GuideWords.Loadout;
                case WmcWing w: return w.Sub == WmcWing.SubStudio ? GuideWords.Studio : w.Sub == WmcWing.SubAircraft ? GuideWords.Aircraft : GuideWords.Roster;
                default: return null;
            }
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
            if (Changed(4, (float.IsNaN(funds) ? -1 : Mathf.RoundToInt(funds * 10f)) * 4 + (sandbox ? 2 : 0) + (context.Client ? 1 : 0)))
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
        private string statusAlert, statusPrompt, statusAmbient;

        private void WriteStatus(string alert, string prompt, string ambient)
        {
            // Same inputs as the last refresh: skip the three concatenations the footer would compare and drop.
            if (alert == statusAlert && prompt == statusPrompt && ambient == statusAmbient) return;
            statusAlert = alert; statusPrompt = prompt; statusAmbient = ambient;
            if (!string.IsNullOrEmpty(prompt)) shell.Footer.Set("ACTION  ·  " + prompt, AvState.Caution);
            else if (!string.IsNullOrEmpty(alert)) shell.Footer.Set("ALERT  ·  " + alert, AvState.Danger);
            else shell.Footer.Set("STATUS  ·  " + (ambient ?? ""), AvState.Inert);
        }
    }
}
