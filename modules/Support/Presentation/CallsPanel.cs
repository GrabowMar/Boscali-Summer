using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using NuclearOption.MissionEditorScripts;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The OPS bezel page, drawn as the SATCOM C2 terminal: the shared <see cref="C2Chrome"/> (banner, header, session line, tabs),
    /// one page per tab and the <see cref="C2Footer"/> that also carries the hover help of every row and button.
    /// [1] CAP is the CALL page (<see cref="CapPage"/>); [2] ORBIT hosts the SPACE feed (<see cref="SpaceFeedPanel"/>);
    /// [5] BOARD lists the live TASKED posts (<see cref="BoardPage"/>); [3] NET (<see cref="CyberNetPage"/>) and [4] SOF (<see cref="SofPage"/>) are the faction-mirror pages; each falls back to honest NO LINK words. Keys 1-5 switch tabs only while the pointer is over the page or the full-screen station is open. The panel owns no policy: every figure comes from
    /// <see cref="SupportManager"/> and every press goes through <see cref="CallsController"/> or <see cref="SpaceFeedController"/>.
    /// </summary>
    internal sealed class CallsPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.15f;

        private readonly List<CallTile> tiles = new List<CallTile>(16);
        private readonly CapView view = new CapView();
        private readonly C2Feed c2 = new C2Feed();
        private readonly RectTransform[] pages = new RectTransform[5];

        private SupportManager manager;
        private CallsController calls;
        private SpaceFeedController feed;
        private SpaceFeedPanel spacePanel;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private RectTransform consoleRoot;
        private AvTicker ticker;
        private C2Chrome chrome;
        private C2Footer footer;
        private CapPage cap;
        private BoardPage boardPage;
        private CyberNetPage netPage;
        private SofPage sofPage;
        private C2Tab tab = C2Tab.Cap;
        private int sceneGeneration;

        private float nextAttempt;
        private float nextRefresh, failedUntil;
        private int factionEpoch;
        private Canvas pageCanvas;
        private int aimCellX = int.MinValue, aimCellZ;
        private string aimGrid = "";
        private bool failed;
        private string chromeKey = "", footerKey = "";

        public void Configure(SupportManager supportManager, CallsController callsController, SpaceFeedController feedController = null)
        {
            manager = supportManager;
            calls = callsController;
            feed = feedController;
            logger = ((ISupportHost)supportManager).Logger;
            c2.Attach(manager, calls);
            feed?.AttachConsole(c2, FillChrome);
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Ops);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            consoleRoot = null;
            ticker = null;
            chrome = null;
            footer = null;
            cap = null;
            boardPage = null;
            netPage = null;
            sofPage = null;
            spacePanel = null;
            tab = C2Tab.Cap;
            manager?.CyberFeed.Want(false);
            sceneGeneration++;
            c2.Clear();
            chromeKey = footerKey = "";
            feed?.SetCompactVisible(false);
            feed?.SetBoardVisible(false);
            feed?.AttachBoard(null);
            nextAttempt = 0f;
            nextRefresh = 0f;
            failedUntil = 0f;
            factionEpoch = c2.FactionEpoch;
            pageCanvas = null;
            failed = false;
        }

        private void OnDestroy()
        {
            ResetForScene();
            c2.Detach();
        }

        private void Update()
        {
            if (failed || manager == null || calls == null) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (Time.unscaledTime < failedUntil) return; // a console fault backs the page off for a moment instead of killing it
            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            feed?.SetCompactVisible(visible && consoleRoot != null && tab == C2Tab.Orbit);
            feed?.SetBoardVisible(visible && consoleRoot != null && tab == C2Tab.Board);
            try
            {
                // The cockpit threat probe is polled only while the page or the station is on screen.
                c2.Tick((visible && consoleRoot != null) || (feed != null && feed.WindowOpen));
                if (c2.FactionEpoch != factionEpoch)
                {
                    factionEpoch = c2.FactionEpoch;
                    if (tab != C2Tab.Cap && consoleRoot != null) SelectTab(C2Tab.Cap); // the old faction's page is not the new faction's
                }
            }
            catch (Exception e)
            {
                failedUntil = Time.unscaledTime + 2f; // a console fault must not throw every frame
                feed?.SetCompactVisible(false);
                feed?.SetBoardVisible(false);
                logger?.LogError("OPS C2 console failed: " + e);
                return;
            }
            if (visible && consoleRoot != null) PollTabKeys();
            if (!visible || consoleRoot == null || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshInterval;
            try { Refresh(); }
            catch (Exception e)
            {
                failed = true; // a refresh fault must not throw every frame
                logger?.LogError("OPS CALLS refresh failed: " + e);
            }
        }

        // ---- Tab keys ---------------------------------------------------------------------------------

        /// <summary>
        /// Digit keys 1-5 pick a tab, but only with the pointer over the page (never in flight: the keys belong to the weapons), never
        /// while typing and never with Ctrl, Alt or Shift held. The cheap <c>anyKeyDown</c> test comes first, so a frame without a
        /// key press never touches the canvas. The station has no tabs, so it never takes them.
        /// </summary>
        private void PollTabKeys()
        {
            if (!Input.anyKeyDown) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;
            if (GameplayUI.GameIsPaused || !Application.isFocused) return;
            if (!C2Tabs.KeyAllowed(PointerOverPage(), false, InputFieldChecker.InsideInputField)) return;
            for (int d = 1; d <= 5; d++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + d) && !Input.GetKeyDown(KeyCode.Keypad0 + d)) continue;
                C2Tab next = C2Tabs.FromKey(d);
                if (next != tab) SelectTab(next);
                return;
            }
        }

        private bool PointerOverPage()
        {
            if (consoleRoot == null || !consoleRoot.gameObject.activeInHierarchy) return false;
            if (pageCanvas == null) pageCanvas = consoleRoot.GetComponentInParent<Canvas>();
            Camera cam = pageCanvas != null && pageCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? pageCanvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(consoleRoot, Input.mousePosition, cam);
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Ops, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger?.LogWarning("OPS MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    failed = true;
                    logger?.LogWarning("OPS MFD unavailable: claimed bezel changed before binding.");
                    return;
                }

                logger?.LogInfo("OPS CALLS MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                MfdBezel.Release(MfdSlots.Ops);
                failed = true;
                logger?.LogError("OPS MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            var root = new GameObject("BoscaliOperations.Screen", typeof(RectTransform));
            screenRoot = root;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvLay.ResolveHeight(templateRect.parent as RectTransform, PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvLay.ClampIntoCanvas(rootRect);

            BuildConsole(rootRect, height);

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Ops;
            result.displayPanel = consoleRoot.gameObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                screenRoot = null;
                consoleRoot = null;
                return null;
            }
            return result;
        }

        /// <summary>Builds the whole page into <paramref name="root"/> without an MFD (offline render harness).</summary>
        internal void BuildForHarness(RectTransform root, float height) => BuildConsole(root, height);

        private void BuildConsole(RectTransform host, float height)
        {
            consoleRoot = AvLay.Child(host, "C2 Terminal");
            AvLay.Place(consoleRoot, 0f, 0f, Width, height);
            AvLay.Nest(consoleRoot, true);
            ticker = consoleRoot.gameObject.AddComponent<AvTicker>();

            AvFrame back = AvFrame.Add(consoleRoot, "Frame", default(AvChamfer));
            AvLay.Fill(back.rectTransform);

            chrome = Reg(new C2Chrome(consoleRoot, SelectTab));
            chrome.Place(new AvSlot(0f, 0f, Width, C2Chrome.Height));

            float pageH = height - C2Chrome.Height - C2Footer.Height;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = AvLay.Child(consoleRoot, "Page " + (i + 1));
                AvLay.Place(pages[i], 0f, C2Chrome.Height, Width, pageH);
            }

            AvTicker t = ticker;
            cap = new CapPage(pages[(int)C2Tab.Cap - 1], Width, pageH, calls, p => t.Register(p));
            spacePanel = new SpaceFeedPanel(pages[(int)C2Tab.Orbit - 1], feed, Width, pageH, false);
            AvLay.Place(spacePanel.Rect, 0f, 0f, Width, pageH);
            ticker.Register(spacePanel);
            feed?.AttachCompact(spacePanel);
            netPage = new CyberNetPage(pages[(int)C2Tab.Net - 1], Width, pageH, calls, p => t.Register(p),
                id => manager?.CyberHop(id), id => manager?.CyberBurn(id), id => manager?.CyberDrop(id), OpsActions());
            sofPage = new SofPage(pages[(int)C2Tab.Sof - 1], Width, pageH, calls, p => t.Register(p), new SofPageActions
            {
                Raise = () => manager?.SofRaise(),
                Order = (slot, verb) => manager?.SofOrder(slot, verb),
                Divert = (slot, x, z) => manager?.SofDivert(slot, x, z),
                Mission = (slot, kind, id, x, z) => manager?.SofMission(slot, kind, id, x, z),
                Pick = (label, onPick) => manager != null && manager.ArmLocalPick(label, point => onPick((float)point.x, (float)point.z)),
                Ops = OpsActions()
            });
            boardPage = new BoardPage(pages[(int)C2Tab.Board - 1], Width, pageH, id => calls?.PressTasked(id), ToggleQuiet, p => t.Register(p));
            feed?.AttachBoard(boardPage);

            footer = Reg(new C2Footer(consoleRoot, () => calls?.Disarm()));
            footer.Place(new AvSlot(0f, height - C2Footer.Height, Width, C2Footer.Height));
            consoleRoot.gameObject.AddComponent<AvHelpScope>().Sink = footer.SetHint;

            CapPage capPage = cap;
            ticker.Register(new Hook(() => { back.Paint(AvStyleHost.FuiColor("ground", Color.black), OpsInk.Frame); capPage.Restyle(); boardPage?.Restyle(); netPage?.Restyle(); sofPage?.Restyle(); }));
            back.Paint(AvStyleHost.FuiColor("ground", Color.black), OpsInk.Frame);
            chromeKey = footerKey = "";
            SelectTab(C2Tab.Cap);
        }

        /// <summary>The OPERATION box's three requests, sent through the manager (a press is a request, never an effect).</summary>
        private OpsBoxActions OpsActions() => new OpsBoxActions
        {
            Fund = (domain, large) => manager?.OpsFund(domain, large),
            Plan = (kind, target) => manager?.OpsPlan(kind, target),
            Cancel = domain => manager?.OpsCancel(domain)
        };

        /// <summary>The QUIET switch of the BOARD page: flips the existing client QuietNotices setting.</summary>
        private void ToggleQuiet()
        {
            SupportSettings s = manager?.Settings;
            if (s != null) s.QuietNotices.Value = !s.QuietNotices.Value;
            nextRefresh = 0f;
        }

        private T Reg<T>(T part) where T : AvPart
        {
            ticker.Register(part);
            return part;
        }

        private sealed class Hook : AvPart
        {
            private readonly Action restyle;
            public Hook(Action restyle) { this.restyle = restyle; }
            public override void Restyle() => restyle?.Invoke();
            public override void Place(AvSlot slot) { }
        }

        private void SelectTab(C2Tab next)
        {
            tab = next;
            manager?.CyberFeed.Want(next == C2Tab.Net); // the NET page is the only reader of the CYBER mirror: it asks the host for a state while it is open
            manager?.SofFeed.Want(next == C2Tab.Sof); // likewise the SOF page for the SOF mirror
            manager?.OpsFeed.Want(next == C2Tab.Net || next == C2Tab.Sof); // and both for the OPERATIONS mirror (their OPERATION box)
            for (int i = 0; i < pages.Length; i++)
                if (pages[i] != null) pages[i].gameObject.SetActive(i == (int)next - 1);
            chromeKey = footerKey = "";
            nextRefresh = 0f;
            if (chrome != null) PaintChrome(tab, view);
        }
        internal BoardPage Board => boardPage;
        internal AvTicker Ticker => ticker;
        internal RectTransform ConsoleRoot => consoleRoot;

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        // ---- Refresh and paint ------------------------------------------------------------

        private void Refresh()
        {
            tiles.Clear();
            int frozen = manager.LocalFrozenSeconds;
            foreach (CallRow row in CallSheet.Rows)
            {
                bool unlocked = manager.Unlocked(row.Id, out string unlock);
                // A pending CALL cannot be tied to one tile from here: the footer carries PENDING, tiles stay as they are.
                CallTile t = CallsView.Tile(row, manager.Quote(row.Id), unlocked, unlock, manager.LocalCredit,
                    manager.LocalCooldownRemaining, calls.Armed == row.Id, false, !manager.Online);
                if (frozen > 0 && t.State != CallState.Offline)
                {
                    int minutes = Mathf.Max(1, Mathf.CeilToInt(frozen / 60f));
                    t = new CallTile(t.Id, t.Label, t.TierWord, t.CostText, t.Reason, CallState.Cooldown,
                        "FROZEN " + minutes + " MIN", false);
                }
                tiles.Add(t);
            }

            FillView();
            Paint(view);
        }

        private void FillView()
        {
            view.Tiles.Clear();
            view.Tiles.AddRange(tiles);
            view.NextUnlock = manager.NextUnlockText();
            view.Words = calls.LastWords;
            view.Pending = calls.Pending;
            for (int i = 0; i < view.Favourites.Length; i++) view.Favourites[i] = i < calls.Favourites.Length ? calls.Favourites[i] : null;
            view.Aim = calls.AimNow;
            view.AimGrid = AimGridNow();
            view.LastDelta = c2.LastDelta;
            view.Cyber = manager.CyberMirror.State;
            view.CyberKnown = manager.CyberMirror.Known;
            view.Sof = manager.SofMirror.State;
            view.SofKnown = manager.SofMirror.Known;
            view.Ops = manager.OpsMirror.State;
            view.OpsKnown = manager.OpsMirror.Known;
            FillChrome(view);
        }

        /// <summary>The armed call's aim as a grid string, rebuilt only when the aim moves into another 100 m cell (the text shows 0.1 km).</summary>
        private string AimGridNow()
        {
            if (!calls.Armed.HasValue || !calls.TryAimPoint(out GlobalPosition p)) { aimCellX = int.MinValue; aimGrid = ""; return ""; }
            int cx = Mathf.RoundToInt((float)(p.x / 100.0)), cz = Mathf.RoundToInt((float)(p.z / 100.0));
            if (cx != aimCellX || cz != aimCellZ) { aimCellX = cx; aimCellZ = cz; aimGrid = TheaterGrid.Kilometres(p.x, p.z); }
            return aimGrid;
        }

        /// <summary>Everything the shared C2 chrome shows (identity, ledger, session line, board count), for the MFD pages and the station.</summary>
        private void FillChrome(C2ChromeView v)
        {
            v.Credit = (int)manager.LocalCredit;
            v.Console = c2.Console;
            v.Alert = c2.Alert;
            v.Link = manager.Online;

            float now = SupportManager.MissionNow();
            v.KeyRot = C2Words.KeyRotation(now);
            if (GameManager.GetLocalPlayer<Player>(out Player player) && player != null)
            {
                v.Faction = player.HQ != null && player.HQ.faction != null ? player.HQ.faction.factionName : "";
                v.Callsign = Callsign(player);
                v.Session = C2Words.Session(PlayerIdentity.Of(player), sceneGeneration);
            }

            SpaceFeedMirror mirror = manager.SpaceMirror;
            SpaceFeedState state = mirror.State;
            if (mirror.Known && state.Active)
            {
                v.Uplinks = state.UplinksLive + "/" + state.UplinksTotal;
                v.UplinkTone = state.UplinksTotal > 0 && state.UplinksLive >= state.UplinksTotal ? AvState.Ready
                    : state.UplinksLive == 0 ? AvState.Danger : AvState.Caution;
                v.Space = C2Feed.SpaceWord(state.Family);
                int open = 0;
                for (int i = 0; i < state.Posts.Count; i++)
                    if (SpaceFeedRules.PostStatusOf(state.Posts[i], now) != PostStatus.Stale) open++; // live = not stale: the badge, the header and the LIVE POSTS box agree
                v.BoardCount = open;
            }
            else
            {
                v.Uplinks = v.Space = "";
                v.UplinkTone = AvState.Inert;
                v.BoardCount = 0;
            }
        }

        private static string Callsign(Player player)
        {
            try { return (player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard) ?? "").ToUpperInvariant(); }
            catch (Exception) { return ""; }
        }

        /// <summary>Paints the chrome, the footer and the CAP page from <paramref name="v"/> (the harness feeds fixtures here).</summary>
        internal void Paint(CapView v)
        {
            if (consoleRoot == null) return;
            PaintChrome(tab, v);
            if (tab == C2Tab.Cap) cap.Paint(v);
            else if (tab == C2Tab.Net) netPage?.Paint(v);
            else if (tab == C2Tab.Sof) sofPage?.Paint(v);
            PaintFooter(v);
        }

        private void PaintChrome(C2Tab t, CapView v)
        {
            string key = string.Concat((int)t, "|", v.Faction, "|", v.Alert, "|", v.Credit, "|", v.Callsign, "|", v.Session, "|", v.KeyRot, "|",
                v.Uplinks, "|", (int)v.UplinkTone, "|", v.Space, "|", v.Link ? "1" : "0", "|", v.BoardCount, "|", C2Cap.CallsReady(v.Tiles), "|", v.CyberKnown && v.Cyber != null ? v.Cyber.Seq : -1, "|", v.SofKnown && v.Sof != null ? v.Sof.Seq : -1, "|", v.OpsKnown && v.Ops != null ? v.Ops.Seq : -1);
            if (key == chromeKey) return;
            chromeKey = key;

            C2Area area = t == C2Tab.Net ? C2Area.Cyber : t == C2Tab.Sof ? C2Area.Sof : C2Area.Orbital;
            bool alert = v.Alert.Length > 0;
            chrome.SetBanner(C2Words.Banner(v.Faction, area), alert ? AvState.Danger : AvState.Caution);
            string title, sub;
            if (t == C2Tab.Net) { title = "NETWORK OPERATIONS"; sub = CyberNetWords.Sub(v.CyberKnown ? v.Cyber : null); }
            else if (t == C2Tab.Sof) { title = "SPECIAL OPERATIONS"; sub = SofPageWords.Sub(v.SofKnown ? v.Sof : null); }
            else if (t == C2Tab.Board) { title = "TASKED BOARD"; sub = "LIVE POSTS · " + v.BoardCount; }
            else if (t == C2Tab.Orbit) { title = "ORBITAL SUPPORT"; sub = "ORBIT · SENSOR, TRACK FILE, TASKED"; }
            else { title = "ORBITAL SUPPORT"; sub = "CAP · " + C2Cap.CallsReady(v.Tiles) + " CALLS READY"; }
            chrome.SetHeader(title, sub, alert ? C2Words.Fit(v.Alert, 18) : null);
            chrome.SetLedger(v.Credit);
            chrome.SetSession(v.Callsign, v.Session, v.KeyRot, v.Uplinks, v.UplinkTone, v.Space, v.Link);
            chrome.SetTabs(t, v.BoardCount);
        }

        private void PaintFooter(CapView v)
        {
            string slab, words;
            AvState tone;
            bool armed = false;
            foreach (CallTile t in v.Tiles) if (t.State == CallState.Armed) { armed = true; break; }
            if (tab == C2Tab.Cap)
            {
                slab = C2Cap.Slab(armed, v.Pending, v.Words);
                words = C2Cap.FooterWords(v.Words, v.NextUnlock);
                tone = armed ? AvState.Caution : v.Pending ? AvState.Info : C2Cap.StartsNegative(v.Words) ? AvState.Danger : AvState.Ready;
            }
            else if (tab == C2Tab.Orbit)
            {
                // The ORBIT page's words (a verdict, a refusal, the enemy intent, a hint) ride the shared footer.
                string said = spacePanel != null ? spacePanel.Words : "";
                tone = spacePanel != null && said.Length > 0 ? spacePanel.WordsTone : AvState.Ready;
                words = said.Length > 0 ? said : "ORBIT FEED · OPEN FULL FOR THE TASKING STATION";
                slab = tone == AvState.Danger ? "NEG" : tone == AvState.Caution ? "WARN" : tone == AvState.Ready ? "READY" : "INT";
            }
            else if (tab == C2Tab.Board && boardPage != null)
            {
                tone = boardPage.WordsTone;
                words = boardPage.Words.Length > 0 ? boardPage.Words : "BOARD CLEAR \u00B7 WAITING FOR A POSTED CALL";
                slab = tone == AvState.Danger ? "NEG" : tone == AvState.Caution ? "WARN" : tone == AvState.Ready ? "READY" : "INT";
            }
            else if (tab == C2Tab.Net || tab == C2Tab.Sof)
            {
                // The domain is offline, but its real CALL rows work: a refusal from one of them replaces the standing (info) words.
                string said = v.Words ?? "";
                string standing = (tab == C2Tab.Net ? netPage?.Words : sofPage?.Words) ?? "";
                bool refused = C2Cap.StartsNegative(said) || C2Cap.StartsNegative(standing); // the page's own standing words may be a NEGATIVE too (CYBER OFFLINE)
                slab = refused ? "NEG" : "INFO";
                tone = refused ? AvState.Danger : AvState.Info;
                words = C2Cap.StartsNegative(said) ? said : standing;
            }
            else { slab = "INT"; tone = AvState.Info; words = ""; }
            // ABORT rides the footer on every page that has no abort of its own (CAP has one in the EXECUTE box) while a call is armed.
            footer.ShowAbort(armed && tab != C2Tab.Cap);
            string key = slab + "|" + (int)tone + "|" + words;
            if (key == footerKey) return;
            footerKey = key;
            footer.Set(slab, tone, words);
        }
    }
}
