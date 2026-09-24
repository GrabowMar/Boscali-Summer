using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Comms.Configuration;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Comms.Presentation
{
    /// <summary>
    /// "COM" — the multiplayer comms screen. Five pages, one job each: MAP arms the pen, the
    /// shapes, pings, stickers and labels; CALL sends brevity calls; POLL asks and answers
    /// questions; GAME holds the dice, rock-paper-scissors and the map hunt with its
    /// leaderboard; LOG is the record of all of it plus the mute list.
    ///
    /// <para>The data bar always says which audience a post will reach (TEAM or ALL), what
    /// the left mouse button will do on the map right now, and how much is on the board; the
    /// status strip gives the armed tool's instructions or the host's latest refusal. Nothing
    /// here decides anything: every verb goes through <see cref="CommsManager"/>, and the host
    /// has the last word.</para>
    /// </summary>
    internal sealed partial class CommsMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.2f;
        private const float NoticeSeconds = 6f;
        private const int ChipCount = 3;

        private const int TabMap = 0;
        private const int TabCall = 1;
        private const int TabPoll = 2;
        private const int TabGame = 3;
        private const int TabLog = 4;
        private static readonly string[] TabNames = { "MAP", "CALL", "POLL", "GAME", "LOG" };

        private const float HeadingHeight = 22f;
        private const float Gap = 4f;
        private const float SectionGap = 8f;

        private CommsSettings settings;
        private CommsManager comms;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvScreen shell;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(CommsSettings config, CommsManager manager, ManualLogSource log)
        {
            settings = config;
            comms = manager;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdScreenHost.Release(MfdSlots.Comms);
            if (screenRoot != null) Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            shell = null;
            if (viewOpen) AvKit.ReleaseKeyboardGuard();
            viewOpen = false;
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            ResetMap();
            ResetTalk();
            ResetGames();
            ResetLog();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || settings == null || comms == null) return;
            if (!settings.Enabled.Value)
            {
                if (screen != null) ResetForScene();
                return;
            }
            if (Application.isBatchMode || !GameAccess.MfdAvailable)
            {
                failed = true;
                return;
            }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            SetViewOpen(visible);
            if (!visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void SetViewOpen(bool open)
        {
            if (viewOpen == open) return;
            viewOpen = open;
            // A text field that loses its screen must hand the keyboard back to the flight controls.
            if (!open) AvKit.ReleaseKeyboardGuard();
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                // The six vanilla slots belong to WMC and the claimed screens; EVN hosts an
                // extra slot on the right, so COM hosts one on the left to keep the columns even.
                if (!MfdScreenHost.TryHost(MfdSlots.Comms, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger?.LogWarning("COM MFD unavailable: could not add a host button.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdScreenHost.Release(MfdSlots.Comms);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdScreenHost.Release(MfdSlots.Comms);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    ResetForScene();
                    failed = true;
                    logger?.LogWarning("COM MFD unavailable: bezel changed during installation.");
                    return;
                }
                logger?.LogInfo("COM MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                ResetForScene();
                failed = true;
                logger?.LogError("COM MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            if (sourceText != null && sourceText.font != null) AvFont.Font = sourceText.font;

            var root = new GameObject("BoscaliComms.Screen", typeof(RectTransform), typeof(Image));
            screenRoot = root;
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvScreen.ResolveHeight(templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvKit.ClampIntoCanvas(rootRect);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            var content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            shell = AvScreen.Build(content, MfdSlots.Comms, TabNames, null, ChipCount, Width, height, _ => nextRefresh = 0f);

            BuildMapPage(shell.CreatePage(TabMap, "MapPage"));
            BuildCallPage(shell.CreatePage(TabCall, "CallPage"));
            BuildPollPage(shell.CreatePage(TabPoll, "PollPage"));
            BuildGamePage(shell.CreatePage(TabGame, "GamePage"));
            BuildLogPage(shell.CreatePage(TabLog, "LogPage"));

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Comms;
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                Destroy(root);
                return null;
            }

            screenRoot = root;
            shell.SetPage(TabMap);
            return result;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void Refresh()
        {
            if (shell == null) return;
            CommsClientState state = comms.State;
            float now = Time.unscaledTime;

            bool online = comms.Online;
            shell.DataBar.State.text = !online ? "NOT CONNECTED"
                : comms.HostSilent ? "HOST NOT ANSWERING"
                : comms.IsHost ? "HOSTING COMMS" : "ONLINE";
            shell.DataBar.State.color = !online || comms.HostSilent ? AvTheme.Warning : AvTheme.Dim;

            shell.DataBar.SetChip(0, comms.Channel == CommsChannel.Team ? "TO TEAM" : "TO ALL",
                comms.Channel == CommsChannel.Team ? "live" : "warn");
            shell.DataBar.SetChip(1, ToolName(comms.Tool), comms.Tool == CommsTool.None ? "inert" : "info");
            int marks = state.Board.Count;
            shell.DataBar.SetChip(2, marks + (marks == 1 ? " MARK" : " MARKS"), marks > 0 ? "live" : "inert");

            switch (shell.Page)
            {
                case TabMap: RefreshMap(); break;
                case TabCall: RefreshCalls(now); break;
                case TabPoll: RefreshPolls(now); break;
                case TabGame: RefreshGames(now); break;
                case TabLog: RefreshLog(now); break;
            }

            string alert = comms.HostSilent
                ? "NO ANSWER FROM THE HOST — COMMS NEEDS BOSCALI SUMMER ON THE HOST TOO"
                : state.Notice != null && state.NoticeIsError && now - state.NoticeAt < NoticeSeconds ? state.Notice : null;
            string prompt = comms.Tool != CommsTool.None ? comms.Prompt(comms.Tool) : null;
            string ambient = state.Notice != null && !state.NoticeIsError && now - state.NoticeAt < NoticeSeconds
                ? state.Notice
                : Ambient(state);
            shell.WriteStatus(alert, prompt, ambient);
        }

        private string Ambient(CommsClientState state)
        {
            int open = 0;
            for (int i = 0; i < state.Polls.Count; i++) if (!state.Polls[i].Closed) open++;
            string line = state.Board.CountOf(CommsItemKind.Ping) + " PINGS · " + open + " POLLS OPEN · " +
                          state.Duels.Count + " CHALLENGES";
            KeyCode hold = settings.DrawHoldKey.Value;
            return hold != KeyCode.None ? line + " · HOLD " + KeyName(hold) + " + DRAG TO DRAW" : line;
        }

        private static string ToolName(CommsTool tool)
        {
            switch (tool)
            {
                case CommsTool.None: return "MAP FREE";
                case CommsTool.HuntHide: return "HIDING";
                case CommsTool.HuntGuess: return "GUESSING";
                case CommsTool.Label: return "TEXT";
                default: return tool.ToString().ToUpperInvariant();
            }
        }

        private static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftShift: return "L-SHIFT";
                case KeyCode.RightShift: return "R-SHIFT";
                case KeyCode.LeftControl: return "L-CTRL";
                case KeyCode.LeftAlt: return "L-ALT";
                case KeyCode.Mouse2: return "MIDDLE MOUSE";
                case KeyCode.Mouse3: return "MOUSE 4";
                case KeyCode.Mouse4: return "MOUSE 5";
                default: return key.ToString().ToUpperInvariant();
            }
        }

        // ---- Shared layout -----------------------------------------------------------------

        /// <summary>A page body inside a scroll viewport when it is taller than the space it has.</summary>
        private RectTransform PageBody(GameObject page, float buildHeight, out float x, out float y, out float width)
        {
            Rect body = shell.Body;
            float viewport = body.height;
            RectTransform parent = AvScreen.Scroll((RectTransform)page.transform, body, buildHeight, out body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, Mathf.Max(viewport, body.height)));
            x = body.x + AvScreen.SpineInset;
            y = body.y;
            width = body.width - AvScreen.SpineInset;
            return parent;
        }

        /// <summary>A section heading: accent tick, title, and a note on the right. Returns the note.</summary>
        private static TMP_Text Heading(RectTransform parent, float x, ref float y, float width, string title, string note = null)
        {
            AvKit.Panel(parent, new Rect(x, y - 1f, 3f, 14f), AvTheme.Accent).raycastTarget = false;
            AvStyled.Label(parent, new Rect(x + 10f, y, width * 0.5f, 14f), title, "section-title");
            TMP_Text noteLabel = AvStyled.Label(parent, new Rect(x + width * 0.35f, y, width * 0.65f, 14f), note ?? "",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
            y -= HeadingHeight;
            return noteLabel;
        }

        /// <summary>Evenly split a row into <paramref name="count"/> cells with <see cref="Gap"/> between them.</summary>
        private static Rect Cell(float x, float y, float width, float height, int count, int index)
        {
            float cell = (width - Gap * (count - 1)) / count;
            return new Rect(x + index * (cell + Gap), y, cell, height);
        }

        private static AvButton Button(RectTransform parent, Rect area, string text, Action action, string tooltip,
            AvButtonStyle style = AvButtonStyle.Default)
        {
            AvButton button = AvStyled.Button(parent, area, text, "btn", action, style);
            if (!string.IsNullOrEmpty(tooltip)) button.WithTooltip(tooltip);
            return button;
        }

        /// <summary>
        /// A button with a vector glyph. Tall buttons stack the glyph over a small caption (the
        /// palettes); short ones put the glyph beside the words (lists and calls).
        /// </summary>
        private static AvButton IconButton(RectTransform parent, Rect area, string glyph, string text, Color tint,
            Action action, string tooltip, out CommsGlyphGraphic icon)
        {
            AvButton button = Button(parent, area, text, action, tooltip);
            var rect = (RectTransform)button.transform;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();

            var go = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(CommsGlyphGraphic));
            go.transform.SetParent(rect, false);
            icon = go.GetComponent<CommsGlyphGraphic>();
            icon.raycastTarget = false;

            if (area.height >= 36f)
            {
                AvKit.Place(icon.rectTransform, new Rect((area.width - 20f) * 0.5f, -4f, 20f, 20f));
                if (label != null)
                {
                    AvKit.Place(label.rectTransform, new Rect(0f, -(area.height - 16f), area.width, 14f));
                    label.fontSizeMax = AvTokens.FontMicro;
                    label.fontSize = AvTokens.FontMicro;
                    label.alignment = TextAlignmentOptions.Center;
                }
            }
            else
            {
                AvKit.Place(icon.rectTransform, new Rect(6f, -(area.height - 16f) * 0.5f, 16f, 16f));
                if (label != null)
                {
                    AvKit.Place(label.rectTransform, new Rect(24f, 0f, area.width - 28f, area.height));
                    label.alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
            icon.Set(glyph, tint, 1.1f);
            return button;
        }

        /// <summary>An empty transform at a rect, so a list row can be shown and hidden as one.</summary>
        private static RectTransform Container(RectTransform parent, Rect area, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, area);
            return rect;
        }

        private static void Show(Component component, bool visible)
        {
            if (component != null && component.gameObject.activeSelf != visible) component.gameObject.SetActive(visible);
        }

        private static Color ToneColour(CommsTone tone) => CommsMesh.Tone(tone);

        private static string Ago(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            if (total < 60) return total + "s";
            if (total < 3600) return total / 60 + "m";
            return total / 3600 + "h";
        }

        private string Who(ulong author, string name) => author == comms.LocalId ? "YOU" : name;
    }
}
