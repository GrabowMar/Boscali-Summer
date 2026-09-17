using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// "STR" — the strategic console on the maximised map.
    ///
    /// <para>This used to be a tab inside OPS, and inside that tab it opened a second tab
    /// bar of its own. Two rows of tabs at different indents, a title and its note drawn
    /// into the same rectangle, doctrine names cut to five characters, and the whole theater
    /// picture in three fixed-height cards — all of it symptoms of one problem, which is
    /// that the strategic layer was a guest in a panel about the player's own perks.</para>
    ///
    /// <para>It is now its own bezel screen with its own bay, and the split follows the
    /// question each answers. OPS is about you: what you have earned, what you may call in.
    /// STR is about the battlefield: merged theater SA (air picture plus frontline), chain
    /// of command, and the theater operations board. Neither needs the other to install.
    /// Faction tasking lives on the ADM screen; STR has no tasking board and no theater
    /// account. CMD names the faction's main effort through TheaterOps; it selects no units
    /// and issues no waypoints.</para>
    ///
    /// <para>The pages show what the mod already computed and previously threw away — the
    /// sortie board, the contested-node list, the frontline's length. Where a figure cannot
    /// be established, it reads as a dash. A zero is a claim, and this panel does not make
    /// claims it has not verified.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        private const int TabSa = 0;
        private const int TabCoc = 1;
        private const int TabCmd = 2;

        private const int ChipCount = 3;

        /// <summary>Contested nodes shown before the list defers to a "and n more" line.</summary>
        private const int NodeRowCount = 8;

        /// <summary>
        /// Combined air picture plus frontline. Taller than the MFD body at lower canvas
        /// heights, so the merged SA page scrolls rather than running under the status strip.
        /// </summary>
        private const float MergedSaContentHeight = 920f;

        private static readonly SortieRole[] Roles =
        {
            SortieRole.Cap, SortieRole.Sead, SortieRole.Cas, SortieRole.Strike, SortieRole.Transit,
        };

        // ---- Dependencies ----------------------------------------------------------------

        private CommandSettings settings;
        private CommandManager command;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private IBaseDefenseAlarmService baseAlarm;
        private IHighCommandView highCommand;
        private ITheaterPriorityView theaterPriority;
        private ITheaterLogisticsView theaterLogistics;

        // ---- Screen ----------------------------------------------------------------------

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvScreen shell;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        // ---- SA page ---------------------------------------------------------------------

        private TMP_Text defconLabel;
        private TMP_Text threatLabel;
        private TMP_Text airCountLabel;
        private Image airBar;
        private TMP_Text sortieNote;
        private readonly TMP_Text[] sortieCount = new TMP_Text[5];
        private readonly Image[] sortieBar = new Image[5];
        private TMP_Text groundValue;
        private TMP_Text airbaseValue;
        private TMP_Text radarValue;

        // ---- FRONT page ------------------------------------------------------------------

        private RectTransform frontRoot;
        private Rect controlBarRect;
        private readonly Image[] controlBarFill = new Image[3];
        private TMP_Text alliedSectors;
        private TMP_Text contestedSectors;
        private TMP_Text hostileSectors;
        private TMP_Text neutralSectors;
        private TMP_Text frontlineValue;
        private TMP_Text nodeValue;
        private readonly ListRow[] nodeRows = new ListRow[NodeRowCount];

        /// <summary>The worst contested nodes, ranked into a reused window each refresh.</summary>
        private readonly TacticalSectorGrid.TacticalNode[] ranked =
            new TacticalSectorGrid.TacticalNode[NodeRowCount];
        private TMP_Text nodeNote;

        // ==================================================================================

        public void Configure(
            CommandSettings config, CommandManager manager, ComMapOverlay mapOverlay,
            ManualLogSource log)
        {
            settings = config;
            command = manager;
            overlay = mapOverlay;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Str);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            shell = null;
            baseAlarm = null;
            highCommand = null;
            theaterPriority = null;
            theaterLogistics = null;

            defconLabel = threatLabel = airCountLabel = sortieNote = null;
            airBar = null;
            Array.Clear(sortieCount, 0, sortieCount.Length);
            Array.Clear(sortieBar, 0, sortieBar.Length);
            groundValue = airbaseValue = radarValue = null;

            frontRoot = null;
            Array.Clear(controlBarFill, 0, controlBarFill.Length);
            alliedSectors = contestedSectors = hostileSectors = neutralSectors = null;
            frontlineValue = nodeValue = nodeNote = null;
            Array.Clear(nodeRows, 0, nodeRows.Length);

            ResetCoc();
            ResetCmd();

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || command == null || settings == null || !settings.Enabled.Value) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            // A closed screen costs nothing. The old theater tab refreshed on every page,
            // including the ones that were not showing it.
            if (!screen.isActive || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd =
                    SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Str, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    // No slot is a crowded bezel, not a broken mod: OPS still installs.
                    failed = true;
                    logger?.LogWarning("STR MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdBezel.Release(MfdSlots.Str);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Str);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Str);
                    if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    failed = true;
                    logger?.LogWarning("STR MFD unavailable: claimed bezel changed before binding.");
                    return;
                }
                logger?.LogInfo("STR MFD installed on " + (left ? "left" : "right") +
                                " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                MfdBezel.Release(MfdSlots.Str);
                failed = true;
                logger?.LogError("STR MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            TMP_FontAsset font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliStrategic.Screen", typeof(RectTransform), typeof(Image));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            // Position is deliberately not copied; see the same note on the OPS screen.
            // VirtualMFD.showPos is zero and MFDScreen.ShowScreen assigns it straight to
            // localPosition, so a screen is placed by its parent and anchors.
            float height = AvScreen.ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
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

            ModServices.TryGet(out baseAlarm);
            ModServices.TryGet(out highCommand);
            ModServices.TryGet(out theaterPriority);
            ModServices.TryGet(out theaterLogistics);

            shell = AvScreen.Build(
                content, "STR",
                new[] { "SA", "COC", "CMD" },
                new[]
                {
                    new[] { "THEATER CONTROL", "HELD" },
                    new[] { "AIR DOMINANCE", "ALLIED" },
                    new[] { "COMMAND", "STAFF" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            // The command metric's caption carries the staff's active/KIA split; the sheet's
            // tracking ellipsised it in the cell. Tighter tracking keeps the full reading.
            if (shell.Metrics.Length > 2 && shell.Metrics[2].Caption != null)
                shell.Metrics[2].Caption.characterSpacing = 0f;

            BuildSaPage(shell.CreatePage(TabSa, "SaPage"));
            BuildCocPage(shell.CreatePage(TabCoc, "CocPage"));
            BuildCmdPage(shell.CreatePage(TabCmd, "CmdPage"));

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = "STR";
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            screenRoot = root;
            shell.SetPage(TabSa);
            return result;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject != button.gameObject) return images[i];
            }
            return button.GetComponent<Image>();
        }

        // ---- Page scaffolding ------------------------------------------------------------

        /// <summary>
        /// A section header on the page spine: a band, a tick, a title, and a right-hand
        /// note. The title and the note get their own halves of the line — drawing both
        /// across the full width is what made the old header overprint itself.
        /// </summary>
        private static float SectionHeader(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
        {
            if (band) AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);

            float titleWidth = width * 0.5f;
            AvStyled.Label(parent, new Rect(x, y, titleWidth, 14f), title, "section-title");

            if (!string.IsNullOrEmpty(note))
            {
                AvStyled.Label(parent, new Rect(x + titleWidth, y, width - titleWidth, 14f),
                               note, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            }

            return y - 22f;
        }

        /// <summary>A label/figure pair on one line, the figure right-aligned against the gutter.</summary>
        private static TMP_Text KeyValue(
            RectTransform parent, float x, float y, float width, string key, string initial = "—")
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.6f, 16f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.6f, y, width * 0.4f, 16f),
                                  initial, "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        private static void Divider(RectTransform parent, float x, float y, float width) =>
            AvKit.Rule(parent, new Rect(x, y, width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        /// <summary>
        /// One pooled list row: a state rail, a name, a wrapping detail line, and a trailing
        /// figure over a bar.
        ///
        /// <para>Pooled rows are a fixed pitch on purpose. The auto-sizing rows elsewhere are
        /// measured against text known at build time; a row whose copy changes every refresh
        /// has nothing to measure, so it is given room for two wrapped detail lines instead
        /// of being re-laid out four times a second.</para>
        /// </summary>
        private sealed class ListRow
        {
            public const float Pitch = 50f;
            private static readonly Color RowHover = new Color(1f, 1f, 1f, 0.06f);

            private readonly GameObject root;
            private readonly Image background;
            private readonly Image rail;
            private readonly TMP_Text name;
            private readonly TMP_Text detail;
            private readonly TMP_Text value;
            private readonly Image bar;
            private readonly AvButton hit;

            public ListRow(RectTransform parent, float x, float y, float width)
            {
                root = new GameObject("ListRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, Pitch - 4f));

                const float trail = 96f;
                float textWidth = width - trail - 20f;

                background = AvKit.Panel(rect, new Rect(0f, 0f, width, Pitch - 4f), Color.clear);
                rail = AvStyled.Rail(rect, new Rect(0f, 0f, 3f, Pitch - 8f), "locked");
                name = AvStyled.Label(rect, new Rect(12f, 0f, textWidth, 15f), "", "row-name");
                detail = AvStyled.Label(rect, new Rect(12f, -16f, textWidth, 28f), "", "row-sub");
                value = AvStyled.Label(rect, new Rect(width - trail, 0f, trail, 15f), "",
                                       "row-value", align: TextAlignmentOptions.MidlineRight);
                bar = AvKit.ProgressBar(rect, new Rect(width - trail, -22f, trail, 6f), 0f,
                                        AvTheme.RailReady);

                Divider(rect, 0f, -(Pitch - 8f), width);
                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, Pitch - 4f), null);
                hit.SetEnabled(false);
                root.SetActive(false);
            }

            public void Bind(string railState, string title, string sub, string figure,
                             float fraction, Color figureColor, Color barColor,
                             Action onClick = null, string tooltip = null)
            {
                rail.color = AvStyleHost.Resolve(
                    AvStyleHost.Style("rail " + railState).Background, AvTheme.RailInert);

                name.text = title ?? "";
                detail.text = sub ?? "";
                value.text = figure ?? "";
                value.color = figureColor;

                bar.color = barColor;
                bar.fillAmount = Mathf.Clamp01(fraction);

                bool clickable = onClick != null;
                hit.SetAction(onClick);
                hit.SetEnabled(clickable);
                hit.WithTooltip(clickable ? tooltip : null);
                hit.SetRowHighlight(background, Color.clear, clickable ? RowHover : Color.clear);

                if (!root.activeSelf) root.SetActive(true);
            }

            public void Hide()
            {
                hit.SetAction(null);
                hit.SetEnabled(false);
                if (root.activeSelf) root.SetActive(false);
            }
        }

        // ---- SA page ---------------------------------------------------------------------

        private void BuildSaPage(GameObject page)
        {
            Rect body = shell.Body;
            // Combined air picture + frontline still exceeds the reduced MFD body at lower
            // canvas heights. Clip and scroll only when needed so the last rows never run
            // under the status strip.
            frontRoot = AvScreen.Scroll((RectTransform)page.transform, body, MergedSaContentHeight, out body);
            float y = body.y;

            AvStyled.Spine(frontRoot, new Rect(body.x, body.y, 3f, body.height));

            y = BuildSaBody(frontRoot, body, y);
            BuildFrontBody(frontRoot, body, y);
        }

        private float BuildSaBody(RectTransform parent, Rect body, float y)
        {
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;

            y = SectionHeader(parent, x, y, width, "AIR PICTURE", "C4ISR", band: false);

            defconLabel = AvStyled.Label(parent, new Rect(x, y, width, 16f),
                                         "DEFCON —", "row-name");
            y -= 18f;
            threatLabel = AvStyled.Label(parent, new Rect(x, y, width, 26f), "", "row-sub");
            y -= 30f;

            airCountLabel = AvStyled.Label(parent, new Rect(x, y, width, 16f), "", "kv-key");
            y -= 18f;
            airBar = AvKit.ProgressBar(parent, new Rect(x, y, width, 6f), 0.5f, AvTheme.RailReady);
            y -= 18f;

            y = SectionHeader(parent, x, y, width, "SORTIE BOARD", "FRIENDLY AI", band: true);

            sortieNote = AvStyled.Label(parent, new Rect(x, y, width, 16f), "", "row-sub");
            y -= 18f;

            for (int i = 0; i < Roles.Length; i++)
            {
                SortieRole role = Roles[i];
                AvStyled.Label(parent, new Rect(x, y, 34f, 16f),
                               SortieClassifier.Code(role), "row-sub");
                AvStyled.Label(parent, new Rect(x + 38f, y, 150f, 16f),
                               SortieClassifier.Name(role), "kv-key");

                sortieBar[i] = AvKit.ProgressBar(
                    parent, new Rect(x + 194f, y - 5f, width - 194f - 44f, 6f), 0f, AvTheme.RailInfo);
                sortieCount[i] = AvStyled.Label(
                    parent, new Rect(x + width - 40f, y, 40f, 16f), "—", "kv-value",
                    align: TextAlignmentOptions.MidlineRight);

                y -= 20f;
            }

            y -= 6f;
            y = SectionHeader(parent, x, y, width, "SURFACE & INFRASTRUCTURE", null, band: false);

            groundValue = KeyValue(parent, x, y, width, "GROUND FORCES  ALLIED / HOSTILE");
            y -= 18f;
            airbaseValue = KeyValue(parent, x, y, width, "AIRBASES  ALLIED / HOSTILE / NEUTRAL");
            y -= 18f;

            // "SAMS" was this number's old label. It is the friendly radar list, so it says so.
            radarValue = KeyValue(parent, x, y, width, "FRIENDLY RADARS ON NET");
            return y - 22f;
        }

        private void RefreshSa(TacticalTheaterState state)
        {
            RefreshSaBody(state);
            RefreshFrontBody(state);
        }

        private void RefreshSaBody(TacticalTheaterState state)
        {
            if (defconLabel == null) return;

            defconLabel.text = "DEFCON " + state.DefconLevel + " · " + state.PrimaryThreatDescription;
            defconLabel.color = AvStyleHost.Resolve(
                AvStyleHost.Style("rail " + TheaterReadout.DefconRail(state.DefconLevel)).Background,
                AvTheme.TextPrimary);

            threatLabel.text = state.ActiveThreatWarning;

            airCountLabel.text = "ALLIED " + state.FriendlyAircraftCount +
                                 "  ·  HOSTILE " + state.HostileAircraftCount +
                                 "  ·  " + TheaterReadout.Percent(state.AirSuperiorityRatio) + " DOMINANCE";
            bool airKnown = !float.IsNaN(state.AirSuperiorityRatio);
            airBar.fillAmount = airKnown ? Mathf.Clamp01(state.AirSuperiorityRatio) : 0f;
            airBar.color = !airKnown ? AvTheme.RailInert
                : state.AirSuperiorityRatio >= 0.5f ? AvTheme.RailReady : AvTheme.RailCaution;

            SortieTally tally = state.Sorties;
            bool known = tally.Observed > 0;

            sortieNote.text = known
                ? tally.Observed + " AI AIRCRAFT OBSERVED · " + tally.Tasked + " TASKED"
                : "NO AI PILOT STATE AVAILABLE — SORTIE ROLES UNKNOWN";
            sortieNote.color = known ? AvTheme.Dim : AvTheme.RailCaution;

            int peak = 1;
            for (int i = 0; i < Roles.Length; i++) peak = Mathf.Max(peak, tally.Of(Roles[i]));

            for (int i = 0; i < Roles.Length; i++)
            {
                int count = tally.Of(Roles[i]);
                sortieCount[i].text = known ? count.ToString() : "—";
                sortieCount[i].color = known && count > 0 ? AvTheme.TextPrimary : AvTheme.Disabled;
                sortieBar[i].fillAmount = known ? count / (float)peak : 0f;
                sortieBar[i].color = Roles[i] == SortieRole.Transit ? AvTheme.RailInert : AvTheme.RailInfo;
            }

            groundValue.text = state.FriendlyGroundUnitsCount + " / " + state.HostileGroundUnitsCount;

            airbaseValue.text = state.FriendlyAirbaseCount + " / " + state.HostileAirbaseCount +
                                " / " + state.NeutralAirbaseCount +
                                (state.ContestedAirbaseCount > 0
                                    ? "   (" + state.ContestedAirbaseCount + " CONTESTED)"
                                    : "");
            airbaseValue.color = state.ContestedAirbaseCount > 0
                ? AvTheme.RailCaution
                : AvTheme.TextPrimary;

            radarValue.text = GameAccess.HqSensorsAvailable ? state.FriendlyRadarCount.ToString() : "—";
        }

        // ---- Frontline (merged into SA) --------------------------------------------------

        private void BuildFrontBody(RectTransform parent, Rect body, float y)
        {
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;

            y = SectionHeader(parent, x, y, width, "SECTOR CONTROL", "LIVE FIELD", band: false);

            controlBarRect = new Rect(x, y, width, 10f);
            AvStyled.Box(parent, controlBarRect, "bar");
            for (int i = 0; i < controlBarFill.Length; i++)
            {
                controlBarFill[i] = AvKit.Panel(parent, new Rect(x, y, 0f, 10f), Color.clear);
            }
            y -= 18f;

            alliedSectors = KeyValue(parent, x, y, width, "ALLIED SECTORS");
            y -= 17f;
            contestedSectors = KeyValue(parent, x, y, width, "CONTESTED SECTORS");
            y -= 17f;
            hostileSectors = KeyValue(parent, x, y, width, "HOSTILE SECTORS");
            y -= 17f;
            neutralSectors = KeyValue(parent, x, y, width, "UNCLAIMED SECTORS");
            y -= 17f;
            frontlineValue = KeyValue(parent, x, y, width, "FRONTLINE LENGTH");
            y -= 17f;
            nodeValue = KeyValue(parent, x, y, width, "TRACKED NODES");
            y -= 22f;

            y = SectionHeader(parent, x, y, width, "CONTESTED GROUND", "BY PRESSURE", band: true);

            nodeNote = AvStyled.Label(parent, new Rect(x, y, width, 16f), "", "row-sub");
            y -= 20f;

            for (int i = 0; i < nodeRows.Length; i++)
            {
                nodeRows[i] = new ListRow(parent, x, y - i * ListRow.Pitch, width);
            }
        }

        private void RefreshFrontBody(TacticalTheaterState state)
        {
            if (alliedSectors == null) return;

            TheaterReadout.Shares(
                state.FriendlySectorCount, state.ContestedSectorCount,
                state.HostileSectorCount, state.NeutralSectorCount,
                out float friendly, out float contested, out float hostile);

            float[] shares = { friendly, contested, hostile };
            Color[] colours = { AvTheme.Accent, AvTheme.RailCaution, AvTheme.RailDanger };

            float cursor = controlBarRect.x;
            for (int i = 0; i < controlBarFill.Length; i++)
            {
                float w = Mathf.Max(0f, controlBarRect.width * shares[i]);
                AvKit.Place(controlBarFill[i].rectTransform,
                            new Rect(cursor, controlBarRect.y, w, controlBarRect.height));
                controlBarFill[i].color = w > 0.5f ? colours[i] : Color.clear;
                cursor += w;
            }

            alliedSectors.text = state.FriendlySectorCount + "   " +
                                 TheaterReadout.Percent(friendly);
            contestedSectors.text = state.ContestedSectorCount + "   " +
                                    TheaterReadout.Percent(contested);
            contestedSectors.color = state.ContestedSectorCount > 0
                ? AvTheme.RailCaution
                : AvTheme.TextPrimary;
            hostileSectors.text = state.HostileSectorCount + "   " +
                                  TheaterReadout.Percent(hostile);
            neutralSectors.text = state.NeutralSectorCount.ToString();

            frontlineValue.text = state.FrontlineSegmentCount > 0
                ? TheaterReadout.Kilometres(state.FrontlineLengthMetres)
                : "NO CONTACT";
            nodeValue.text = state.TotalNodesCount + " / " + TacticalSectorGrid.MaximumNodes;

            RefreshNodeRows();
        }

        private void RefreshNodeRows()
        {
            TacticalSectorGrid grid = overlay != null ? overlay.Grid : null;
            if (grid == null)
            {
                nodeNote.text = "SECTOR FIELD NOT RUNNING.";
                nodeNote.color = AvTheme.Dim;
                for (int i = 0; i < nodeRows.Length; i++) nodeRows[i].Hide();
                return;
            }

            IReadOnlyList<TacticalSectorGrid.TacticalNode> nodes = grid.GetNodes();

            // Keep the worst few by insertion into a fixed window. The catalogue is capped
            // at 128 nodes and the window at 8, so this is a bounded pass with no allocation
            // and no full sort of a list that is mostly not contested.
            int contestedTotal = 0;
            int pressingTotal = 0;
            int shown = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].IsContested) continue;
                contestedTotal++;
                if (nodes[i].CaptureProgress >= 0.05f) pressingTotal++;

                float pressure = nodes[i].CaptureProgress;

                int slot = shown;
                while (slot > 0 && ranked[slot - 1].CaptureProgress < pressure) slot--;
                if (slot >= nodeRows.Length) continue;

                for (int j = Mathf.Min(shown, nodeRows.Length - 1); j > slot; j--)
                {
                    ranked[j] = ranked[j - 1];
                }

                ranked[slot] = nodes[i];
                if (shown < nodeRows.Length) shown++;
            }

            for (int i = 0; i < shown; i++)
            {
                TacticalSectorGrid.TacticalNode node = ranked[i];
                bool friendly = node.Faction == SectorControl.Friendly;

                // Pressure on ground we hold is bad news; pressure on ground they hold is
                // progress. Same number, opposite meaning, so the rail cannot be the only
                // thing that says which.
                Color tint = friendly ? AvTheme.RailCaution : AvTheme.RailReady;

                bool pressing = node.CaptureProgress >= 0.05f;
                nodeRows[i].Bind(
                    TheaterReadout.NodeRail(friendly, node.IsContested),
                    string.IsNullOrEmpty(node.Name) ? "UNNAMED NODE" : node.Name.ToUpperInvariant(),
                    (node.IsAirbase ? "AIRBASE" : "STRONGPOINT") + " · " +
                    (friendly ? "ALLIED HELD" : "HOSTILE HELD") + " · " +
                    TheaterReadout.PressureState(node.CaptureProgress),
                    pressing ? TheaterReadout.Percent(node.CaptureProgress) : "—",
                    pressing ? node.CaptureProgress : 0f,
                    tint, tint);
            }

            for (int i = shown; i < nodeRows.Length; i++) nodeRows[i].Hide();

            if (contestedTotal == 0)
            {
                nodeNote.text = "NO CONTESTED GROUND. THE LINE IS QUIET.";
                nodeNote.color = AvTheme.Dim;
            }
            else
            {
                nodeNote.text = contestedTotal + " NODE" + (contestedTotal == 1 ? "" : "S") +
                                " IN CONTACT" +
                                (pressingTotal > 0 ? " · " + pressingTotal + " UNDER PRESSURE" : "") +
                                (contestedTotal > shown ? " · SHOWING TOP " + shown : "");
                nodeNote.color = AvTheme.RailCaution;
            }
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (command == null || shell == null) return;

            highCommand?.Refresh();
            theaterPriority?.Refresh();
            theaterLogistics?.Refresh();

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            FactionHQ hq = map != null ? map.HQ : null;
            if (hq != null) command.UpdateTelemetry(hq);

            TacticalTheaterState state = command.TheaterState;

            RefreshChrome(state);

            switch (shell.Page)
            {
                case TabSa: RefreshSa(state); break;
                case TabCoc: RefreshCoc(); break;
                case TabCmd: RefreshCmd(); break;
            }

            shell.WriteStatus(
                baseAlarm != null ? baseAlarm.ActiveAlertTicker : null,
                MapPicker.Prompt,
                Ambient(state));
        }

        private void RefreshChrome(TacticalTheaterState state)
        {
            shell.DataBar.State.text = state.PrimaryThreatDescription;
            shell.DataBar.State.color = state.DefconLevel <= 2 ? AvTheme.Alert
                                      : state.DefconLevel == 3 ? AvTheme.Warning
                                      : AvTheme.Dim;

            shell.DataBar.SetChip(
                0,
                "DEFCON " + state.DefconLevel,
                state.DefconLevel <= 2 ? "danger" : state.DefconLevel == 3 ? "warn" : "live");
            bool frontline = state.ContestedSectorCount > 0;
            shell.DataBar.SetChip(1, frontline ? "FRONT LIVE" : "FRONT QUIET", frontline);
            bool grid = settings != null && settings.FrontlinesOverlay.Value;
            shell.DataBar.SetChip(2, grid ? "GRID ON" : "GRID OFF", grid);

            bool territoryKnown = !float.IsNaN(state.TerritoryControlRatio);
            // The allied/hostile split rides the unit slot. The caption has room for the pair's
            // labels or for both counts, not for both — and a reading that gets ellipsised is
            // a reading the panel did not give.
            shell.Metrics[0].Unit.text = state.FriendlySectorCount + "/" + state.HostileSectorCount;
            shell.Metrics[0].Set(
                TheaterReadout.Percent(state.TerritoryControlRatio),
                "ALLIED / HOSTILE",
                territoryKnown ? state.TerritoryControlRatio : 0f,
                !territoryKnown ? AvTheme.RailInert
                    : state.TerritoryControlRatio >= 0.5f ? AvTheme.RailReady : AvTheme.RailCaution);

            bool airKnown = !float.IsNaN(state.AirSuperiorityRatio);
            shell.Metrics[1].Unit.text = state.FriendlyAircraftCount + "/" + state.HostileAircraftCount;
            shell.Metrics[1].Set(
                TheaterReadout.Percent(state.AirSuperiorityRatio),
                "ALLIED / HOSTILE",
                airKnown ? state.AirSuperiorityRatio : 0f,
                !airKnown ? AvTheme.RailInert
                    : state.AirSuperiorityRatio >= 0.5f ? AvTheme.RailReady : AvTheme.RailCaution);

            // The third pillar of the theater picture: without a staff, no command effect.
            if (shell.Metrics.Length > 2)
            {
                bool staff = highCommand != null && highCommand.Available;
                float cohesion = staff ? Mathf.Clamp01(highCommand.FriendlyCohesion) : 0f;
                shell.Metrics[2].Set(
                    staff ? TheaterReadout.Percent(cohesion) : "—",
                    // The caption has room for the active count or for the pair; a staff with no
                    // losses reads as "6 ACTIVE" rather than spending the cell on a zero.
                    staff
                        ? highCommand.FriendlyActive + " ACTIVE" +
                          (highCommand.FriendlyKia > 0
                              ? " · " + highCommand.FriendlyKia + " KIA"
                              : "")
                        : "NO STAFF",
                    cohesion,
                    !staff ? AvTheme.RailInert
                    : cohesion >= 0.6f ? AvTheme.RailReady
                    : cohesion >= 0.3f ? AvTheme.RailCaution
                    : AvTheme.RailDanger);
            }
        }

        private string Ambient(TacticalTheaterState state)
        {
            string text = state.ContestedSectorCount > 0
                ? state.ContestedSectorCount + " contested sector" +
                  (state.ContestedSectorCount == 1 ? "" : "s") + " · frontline " +
                  TheaterReadout.Kilometres(state.FrontlineLengthMetres)
                : "No contested ground";

            if (highCommand != null && highCommand.Available)
            {
                text += " · command " + TheaterReadout.Percent(Mathf.Clamp01(highCommand.FriendlyCohesion));
                // The staff's own signal (a stipend, a commendation, a kill) is the COC page's
                // feedback line; it rides the pinned status strip instead of costing a row.
                if (shell != null && shell.Page == TabCoc && !string.IsNullOrEmpty(highCommand.Signal))
                    text += " · " + highCommand.Signal;
            }
            else if (shell != null && shell.Page == TabCoc)
            {
                // The page reads "STAFF NOT RUNNING" in one line. The reason is the host's own
                // status sentence, which only the strip has room for.
                text += " · " + (highCommand == null
                    ? "chain of command is not running on this host"
                    : highCommand.Status ?? "chain of command is forming");
            }

            if (shell != null && shell.Page == TabCmd)
            {
                text += " · " + (theaterPriority == null || !theaterPriority.Available
                    ? "theater operations not running"
                    : theaterPriority.HasPriority
                        ? "main effort " + theaterPriority.PriorityLabel
                        : theaterPriority.CanCommand
                            ? "no main effort set"
                            : "main effort is host-set");
            }
            return text;
        }
    }
}
