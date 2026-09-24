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
    ///
    /// <para>Every page fills the bay it is given. The fixed blocks are laid out from the
    /// top, and the page's primary list — contested ground on SA, the objective and
    /// reinforcement boards on CMD — takes whatever height is left, so a short canvas
    /// scrolls and a tall one never ends in a dead strip above the status line.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        private const int TabSa = 0;
        private const int TabCoc = 1;
        private const int TabCmd = 2;

        private const int ChipCount = 3;

        /// <summary>Contested nodes the SA window can rank; the visible slice is sized to the bay.</summary>
        private const int NodeRowCount = 8;

        /// <summary>The fewest contested rows a page shows before it would rather scroll.</summary>
        private const int NodeRowMinimum = 3;

        /// <summary>A taller bay spreads rows to this pitch at most, never into a dead band.</summary>
        private const float NodePitchMax = 52f;
        private const float ListPitch = 44f;

        // ---- Shared page geometry --------------------------------------------------------

        /// <summary>What one <see cref="SectionHeader"/> consumes, title, note and rule.</summary>
        private const float HeaderStep = 24f;

        /// <summary>The pitch of one SORTIE BOARD row; the board is a fixed five-role table.</summary>
        private const float SortiePitch = 26f;

        /// <summary>The pitch of one SECTOR CONTROL legend row.</summary>
        private const float LegendPitch = 22f;

        /// <summary>The pitch of a label/figure pair on one line.</summary>
        private const float KvPitch = 22f;

        /// <summary>
        /// Everything the SA page holds above the contested list, in the order the build
        /// lays it out. Written as the sum of the same steps the layout uses, so the list
        /// can be sized against the bay without measuring text at build time.
        /// </summary>
        private const float SaFixedHeight =
            HeaderStep + 18f + 20f + 16f + 14f +                            // AIR PICTURE
            HeaderStep + 16f + 5f * SortiePitch + 6f +                       // SORTIE BOARD
            HeaderStep + 3f * KvPitch + 4f +                                 // SURFACE & INFRASTRUCTURE
            HeaderStep + 12f + 4f * LegendPitch + 6f + 2f * KvPitch + 4f +   // SECTOR CONTROL
            HeaderStep + 16f + 2f;                                           // CONTESTED GROUND

        private static readonly SortieRole[] Roles =
        {
            SortieRole.Cap, SortieRole.Sead, SortieRole.Cas, SortieRole.Strike, SortieRole.Transit,
        };

        // ---- Shared row tints ------------------------------------------------------------

        // One hover wash and one dimmed-portrait tint for every page: the SA list rows and
        // the COC roster/dossier share them, so a tint tweak lands everywhere at once.
        private static readonly Color RowHover = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color PortraitDim = new Color(1f, 1f, 1f, 0.45f);

        // ---- Dependencies ----------------------------------------------------------------

        private CommandSettings settings;
        private CommandManager command;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private IBaseDefenseAlarmService baseAlarm;
        private IHighCommandView highCommand;
        private IActiveEventsView activeEvents;
        private ITheaterStrikePicture strikePicture;
        private ITheaterPriorityView theaterPriority;
        private ITheaterLogisticsView theaterLogistics;
        private ITheaterOperationsView theaterOperations;

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
        private GameObject airBarRoot;
        private Image airFill;
        private Image saAlertRail;
        private TMP_Text sortieNote;
        private readonly SortieRow[] sortieRows = new SortieRow[5];
        private TMP_Text groundValue;
        private TMP_Text airbaseValue;
        private TMP_Text radarValue;

        // ---- FRONT page ------------------------------------------------------------------

        private RectTransform frontRoot;
        private Rect controlBarRect;
        private readonly Image[] controlBarFill = new Image[3];
        private readonly SectorLegend[] sectorLegend = new SectorLegend[4];
        private TMP_Text frontlineValue;
        private TMP_Text nodeValue;
        private readonly ListRow[] nodeRows = new ListRow[NodeRowCount];
        private int nodeRowsBuilt;
        private float nodeRowPitch = AvTokens.RowPitch;

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
            theaterOperations = null;

            defconLabel = threatLabel = airCountLabel = sortieNote = null;
            airBarRoot = null;
            airFill = null;
            saAlertRail = null;
            Array.Clear(sortieRows, 0, sortieRows.Length);
            groundValue = airbaseValue = radarValue = null;

            frontRoot = null;
            Array.Clear(controlBarFill, 0, controlBarFill.Length);
            Array.Clear(sectorLegend, 0, sectorLegend.Length);
            frontlineValue = nodeValue = nodeNote = null;
            nodeRowsBuilt = 0;
            nodeRowPitch = AvTokens.RowPitch;
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
            ModServices.TryGet(out activeEvents);
            ModServices.TryGet(out strikePicture);
            ModServices.TryGet(out theaterPriority);
            ModServices.TryGet(out theaterLogistics);
            ModServices.TryGet(out theaterOperations);

            shell = AvScreen.Build(
                content, "STR",
                new[] { "SITUATION", "COMMAND", "OPERATIONS" },
                new[]
                {
                    new[] { "THEATER CONTROL", "HELD" },
                    new[] { "AIR DOMINANCE", "ALLIED" },
                    new[] { "COMMAND", "STAFF" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            // The panel's primary navigation is also a control, and every control explains
            // itself on the status strip.
            if (shell.Tabs.Length > 0)
                shell.Tabs[0].WithTooltip("Air picture, sortie board and sector control.");
            if (shell.Tabs.Length > 1)
                shell.Tabs[1].WithTooltip("Chain of command: posts, personnel files and the staff log.");
            if (shell.Tabs.Length > 2)
                shell.Tabs[2].WithTooltip("Theater operations: offensives, main effort and reinforcement calls.");
            string[] tabGlyphs = { "theater", "person", "flag" };
            for (int i = 0; i < shell.Tabs.Length && i < tabGlyphs.Length; i++)
                DecorateStrTab(shell.Tabs[i], tabGlyphs[i]);

            // A caption that gets ellipsised is a reading the panel did not give. The sheet
            // tracks captions for the display type, and the counts they carry ("3264/3188",
            // "6 ACTIVE · 2 KIA") are long enough that the tracking closes the last glyph
            // off. The row drops tracking on the captions only, so the three cells keep one
            // key, value, unit and caption baseline.
            for (int i = 0; i < shell.Metrics.Length; i++)
                if (shell.Metrics[i].Caption != null) shell.Metrics[i].Caption.characterSpacing = 0f;

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

        private static void DecorateStrTab(AvButton tab, string kind)
        {
            if (tab == null) return;
            var root = (RectTransform)tab.transform;
            // The shell's layout has not necessarily reached the Canvas pass yet.
            float width = root.sizeDelta.x > 1f ? root.sizeDelta.x : root.rect.width;
            float height = root.sizeDelta.y > 1f ? root.sizeDelta.y : root.rect.height;
            if (width <= 1f) width = Width / 3f;
            if (height <= 1f) height = AvTokens.TabBarHeight;
            var icon = new GameObject("TabIcon", typeof(RectTransform), typeof(MfdGlyph));
            var rect = (RectTransform)icon.transform;
            rect.SetParent(root, false);
            AvKit.Place(rect, new Rect(8f, -(height - 14f) * .5f, 14f, 14f));
            var glyph = icon.GetComponent<MfdGlyph>();
            glyph.raycastTarget = false;
            glyph.SetKind(kind, AvTheme.RailInfo);
            TMP_Text label = tab.GetComponentInChildren<TMP_Text>();
            if (label == null) return;
            AvKit.Place(label.rectTransform, new Rect(26f, 0f, width - 30f, height));
            label.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static string SectionGlyph(string title)
        {
            if (title.Contains("AIR") || title.Contains("SORTIE")) return "air";
            if (title.Contains("SECTOR") || title.Contains("GROUND")) return "control";
            if (title.Contains("COMMAND") || title.Contains("STAFF")) return "person";
            if (title.Contains("OPERATIONS") || title.Contains("AXES")) return "flag";
            if (title.Contains("REINFORCE") || title.Contains("READINESS")) return "convoy";
            return "theater";
        }

        private static void SectionIcon(RectTransform parent, float x, float y, string title)
        {
            var icon = new GameObject("SectionIcon", typeof(RectTransform), typeof(MfdGlyph));
            var rect = (RectTransform)icon.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, new Rect(x, y - 1f, 13f, 13f));
            var glyph = icon.GetComponent<MfdGlyph>();
            glyph.raycastTarget = false;
            glyph.SetKind(SectionGlyph(title), AvTheme.RailInfo);
        }

        /// <summary>
        /// The one section header every page wears: a spine tick, the title, the live note
        /// on the right of the same line, and a hairline rule under both. The title and the
        /// note get their own halves of the line — drawing both across the full width is
        /// what made the old header overprint itself — and every section on every page is
        /// aligned to the same left column.
        /// </summary>
        private static float SectionHeader(
            RectTransform parent, float x, float y, float width, string title, string note,
            bool band, out TMP_Text noteLabel)
        {
            // Every section uses one quiet rule; bands looked like another navigation row.
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);

            float titleWidth = width * 0.56f;
            SectionIcon(parent, x, y, title);
            AvStyled.Label(parent, new Rect(x + 19f, y, titleWidth - 19f, 14f), title, "section-title");
            // The slot exists on every header, even one with nothing to say yet: a live
            // count then has somewhere to land without a second layout pass.
            noteLabel = AvStyled.Label(parent, new Rect(x + titleWidth, y, width - titleWidth, 14f),
                                       note ?? "", "section-title-note",
                                       align: TextAlignmentOptions.MidlineRight);
            Divider(parent, x, y - 17f, width);
            return y - HeaderStep;
        }

        private static float SectionHeader(
            RectTransform parent, float x, float y, float width, string title, string note, bool band) =>
            SectionHeader(parent, x, y, width, title, note, band, out _);

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
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));

        /// <summary>
        /// One row of the sortie board: the role on the left, its count right-aligned, and a
        /// short track under the figure for the role's share of the AI aircraft that were
        /// actually observed. There is no rule drawn from the name to the number: that long
        /// fill read as a data bar while carrying no figure of its own.
        /// </summary>
        private sealed class SortieRow
        {
            private const float ValueWidth = 64f;

            private readonly TMP_Text name;
            private readonly TMP_Text value;
            private readonly Image track;
            private readonly Image fill;

            public SortieRow(RectTransform parent, float x, float y, float width)
            {
                var root = new GameObject("SortieRow", typeof(RectTransform));
                var rect = (RectTransform)root.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, SortiePitch));

                name = AvStyled.Label(rect, new Rect(0f, 0f, width - ValueWidth - AvTokens.Space2, 14f),
                                      "", "row-name");
                value = AvStyled.Label(rect, new Rect(width - ValueWidth, 0f, ValueWidth, 14f),
                                       "—", "row-value", align: TextAlignmentOptions.MidlineRight);
                track = AvKit.Panel(rect, new Rect(width - ValueWidth, -15f, ValueWidth, 3f),
                                    AvTheme.SurfaceInert);
                fill = AvKit.Panel(rect, new Rect(width - ValueWidth + 1f, -16f, ValueWidth - 2f, 1f),
                                   AvTheme.RailInfo);
                fill.sprite = AvSprites.White;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;

                Divider(rect, 0f, -(SortiePitch - 1f), width);
            }

            /// <summary>
            /// Bind the row. <paramref name="known"/> false hides the track entirely: a bar
            /// beside a dash is the panel making a claim it could not verify.
            /// </summary>
            public void Bind(string role, string figure, bool known, float share, Color colour)
            {
                name.text = role;
                value.text = figure;
                value.color = known ? AvTheme.TextPrimary : AvTheme.Disabled;

                if (track.gameObject.activeSelf != known) track.gameObject.SetActive(known);
                if (fill.gameObject.activeSelf != known) fill.gameObject.SetActive(known);
                if (!known) return;

                fill.color = colour;
                fill.fillAmount = Mathf.Clamp01(share);
            }
        }

        /// <summary>
        /// One row of the sector-control legend: a colour mark, the side it belongs to, the
        /// sector count and the share, on one column grid so every figure starts and ends on
        /// the same edge as the figure above it.
        /// </summary>
        private sealed class SectorLegend
        {
            private const float CountWidth = 56f;
            private const float ShareWidth = 52f;

            private readonly Image mark;
            private readonly TMP_Text key;
            private readonly TMP_Text count;
            private readonly TMP_Text share;

            public SectorLegend(RectTransform parent, float x, float y, float width)
            {
                var root = new GameObject("SectorLegend", typeof(RectTransform));
                var rect = (RectTransform)root.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, LegendPitch));

                // A mark, not a rail: the rail language means state everywhere else, and
                // this one's only job is to key the share bar's colours.
                mark = AvKit.Rule(rect, new Rect(0f, -6f, 10f, 6f), AvTheme.RailInert);
                float keyWidth = width - CountWidth - ShareWidth - 16f;
                key = AvStyled.Label(rect, new Rect(14f, 0f, keyWidth, 14f), "", "kv-key");
                count = AvStyled.Label(rect, new Rect(width - CountWidth - ShareWidth, 0f, CountWidth, 14f),
                                       "—", "row-value", align: TextAlignmentOptions.MidlineRight);
                share = AvStyled.Label(rect, new Rect(width - ShareWidth, 0f, ShareWidth, 14f),
                                       "—", "row-value-unit", align: TextAlignmentOptions.MidlineRight);
            }

            public void Bind(string label, string countText, string shareText, Color colour, Color countColour)
            {
                key.text = label;
                count.text = countText;
                count.color = countColour;
                share.text = shareText;
                mark.color = colour;
            }
        }

        /// <summary>
        /// One pooled list row: a state rail, a name, a state line, a trailing figure, and an
        /// optional track under it.
        ///
        /// <para>The track is shown only where the caller has a fraction that means
        /// something; a row with no figure reads as a dash and its state line, never as a
        /// bare bar. Rows keep a fixed pitch so a refresh reuses them instead of rebuilding
        /// the list four times a second.</para>
        /// </summary>
        private sealed class ListRow
        {
            private readonly GameObject root;
            private readonly Image background;
            private readonly Image rail;
            private readonly TMP_Text name;
            private readonly TMP_Text detail;
            private readonly TMP_Text value;
            private readonly Image track;
            private readonly Image fill;
            private readonly Image divider;
            private readonly AvButton hit;
            private readonly float width;
            private float height;

            public ListRow(RectTransform parent, float x, float y, float width, float pitch)
            {
                this.width = width;
                height = pitch - 2f;

                root = new GameObject("ListRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, height));

                const float trail = 64f;
                float textWidth = width - trail - AvTokens.Space3;

                background = AvKit.Panel(rect, new Rect(0f, 0f, width, height), Color.clear);
                rail = AvStyled.Rail(rect, new Rect(0f, -2f, 3f, Mathf.Max(8f, height - 6f)), "locked");
                // The name and its state line are one row tall, so a clipped tail must be an
                // ellipsis on that row rather than a second line printed over the next one.
                name = AvStyled.Label(rect, new Rect(12f, -3f, textWidth, 16f), "", "row-name");
                detail = AvStyled.Label(rect, new Rect(12f, -21f, textWidth, 16f), "", "row-sub");
                detail.enableWordWrapping = false;
                detail.overflowMode = TextOverflowModes.Ellipsis;
                value = AvStyled.Label(rect, new Rect(width - trail, 0f, trail, 14f), "",
                                       "row-value", align: TextAlignmentOptions.MidlineRight);
                track = AvKit.Panel(rect, new Rect(width - trail, -17f, trail, 3f), AvTheme.SurfaceInert);
                fill = AvKit.Panel(rect, new Rect(width - trail + 1f, -18f, trail - 2f, 1f),
                                   AvTheme.RailInert);
                fill.sprite = AvSprites.White;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;

                divider = AvKit.Rule(rect, new Rect(0f, -(height - 2f), width, 1f),
                                     AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));
                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, height), null);
                hit.SetEnabled(false);
                root.SetActive(false);
            }

            /// <summary>
            /// Moves the row to a new line of the page. The page re-flows its lists when the
            /// data changes length, so a row has to be placeable more than once; only the
            /// pitch changes, which is the rail's height and the closing hairline.
            /// </summary>
            public void Place(float x, float y, float pitch)
            {
                height = pitch - 2f;
                var rect = (RectTransform)root.transform;
                AvKit.Place(rect, new Rect(x, y, width, height));
                AvKit.Place(background.rectTransform, new Rect(0f, 0f, width, height));
                AvKit.Place(rail.rectTransform, new Rect(0f, -2f, 3f, Mathf.Max(8f, height - 6f)));
                AvKit.Place(divider.rectTransform, new Rect(0f, -(height - 2f), width, 1f));
                AvKit.Place((RectTransform)hit.transform, new Rect(0f, 0f, width, height));
            }

            public void Bind(string railState, string title, string sub, string figure,
                             bool showTrack, float fraction, Color figureColor, Color trackColor,
                             Action onClick = null, string tooltip = null)
            {
                rail.color = AvStyleHost.Resolve(
                    AvStyleHost.Style("rail " + railState).Background, AvTheme.RailInert);

                name.text = title ?? "";
                detail.text = sub ?? "";
                value.text = figure ?? "";
                value.color = figureColor;

                if (track.gameObject.activeSelf != showTrack) track.gameObject.SetActive(showTrack);
                if (fill.gameObject.activeSelf != showTrack) fill.gameObject.SetActive(showTrack);
                if (showTrack)
                {
                    fill.color = trackColor;
                    fill.fillAmount = Mathf.Clamp01(fraction);
                }

                bool clickable = onClick != null;
                hit.SetAction(onClick);
                hit.SetEnabled(clickable);
                // A row that cannot be clicked still explains itself on hover: the reason
                // belongs on the status strip, not in a missing cursor.
                hit.WithTooltip(string.IsNullOrEmpty(tooltip) ? null : tooltip);
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
            Rect view = shell.Body;

            // The page fills the bay. When the fixed blocks leave room for the contested
            // list, the list takes what fits and its pitch spreads to meet the status line
            // rather than ending in a dead strip; when the bay is short the full window is
            // built and the page scrolls instead of silently showing fewer nodes than the
            // list is allowed to name.
            float space = view.height - SaFixedHeight;
            bool fits = space >= NodeRowMinimum * ListPitch;
            nodeRowsBuilt = fits
                ? Mathf.Clamp(Mathf.FloorToInt(space / ListPitch), NodeRowMinimum, NodeRowCount)
                : NodeRowCount;
            nodeRowPitch = fits
                ? Mathf.Clamp(space / nodeRowsBuilt, ListPitch, NodePitchMax)
                : ListPitch;

            float contentHeight = Mathf.Max(view.height, SaFixedHeight + nodeRowsBuilt * nodeRowPitch);
            Rect body;
            frontRoot = AvScreen.Scroll((RectTransform)page.transform, view, contentHeight, out body);

            AvStyled.Spine(frontRoot, new Rect(body.x, body.y, 3f, body.height));

            float y = BuildSaBody(frontRoot, body, body.y);
            BuildFrontBody(frontRoot, body, y);
        }

        private float BuildSaBody(RectTransform parent, Rect body, float y)
        {
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;

            y = SectionHeader(parent, x, y, width, "AIR PICTURE", "C4ISR", band: false);

            // The alert is a single instrument: classification, warning and air balance.
            // Draw it before the text so its frame stays below the readings.
            AvKit.Panel(parent, new Rect(x, y + 3f, width, 67f), AvTheme.Surface);
            saAlertRail = AvKit.Rule(parent, new Rect(x, y + 3f, 3f, 67f), AvTheme.RailInert);

            defconLabel = AvStyled.Label(parent, new Rect(x + 10f, y, width - 20f, 16f),
                                         "DEFCON —", "row-name");
            y -= 18f;
            threatLabel = AvStyled.Label(parent, new Rect(x + 10f, y, width - 20f, 18f), "", "row-main");
            y -= 20f;

            airCountLabel = AvStyled.Label(parent, new Rect(x + 10f, y, width - 20f, 14f), "", "kv-key");
            y -= 16f;
            airBarRoot = BuildAirTrack(parent, x + 10f, y, width - 20f);
            y -= 14f;

            y = SectionHeader(parent, x, y, width, "SORTIE BOARD", "FRIENDLY AI", band: true);

            sortieNote = AvStyled.Label(parent, new Rect(x, y, width, 14f), "", "row-sub");
            y -= 16f;

            for (int i = 0; i < Roles.Length; i++)
            {
                sortieRows[i] = new SortieRow(parent, x, y - i * SortiePitch, width);
            }
            y -= Roles.Length * SortiePitch + 6f;

            y = SectionHeader(parent, x, y, width, "SURFACE & INFRASTRUCTURE", "ALLIED / HOSTILE",
                              band: false);

            groundValue = KeyValue(parent, x, y, width, "GROUND FORCES");
            y -= KvPitch;
            airbaseValue = KeyValue(parent, x, y, width, "AIRBASES  ALLIED / HOSTILE / NEUTRAL");
            y -= KvPitch;

            // "SAMS" was this number's old label. It is the friendly radar list, so it says so.
            radarValue = KeyValue(parent, x, y, width, "FRIENDLY RADARS ON NET");
            return y - KvPitch - 4f;
        }

        /// <summary>
        /// The air-dominance track. It is its own object so the whole track can be hidden
        /// when the ratio is unknown: an empty track under a dash would read as a measured
        /// zero, which is exactly what the dash is there to refuse.
        /// </summary>
        private GameObject BuildAirTrack(RectTransform parent, float x, float y, float width)
        {
            var root = new GameObject("AirTrack", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, new Rect(x, y, width, 4f));

            AvKit.Panel(rect, new Rect(0f, 0f, width, 4f), AvTheme.SurfaceInert);
            airFill = AvKit.Panel(rect, new Rect(1f, -1f, width - 2f, 2f), AvTheme.RailCaution);
            airFill.sprite = AvSprites.White;
            airFill.type = Image.Type.Filled;
            airFill.fillMethod = Image.FillMethod.Horizontal;
            airFill.fillOrigin = 0;
            return root;
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
            if (saAlertRail != null) saAlertRail.color = defconLabel.color;

            // The alert line carries the state in words and the ink follows the same scale,
            // so the warning is never a colour with nothing said.
            threatLabel.text = state.ActiveThreatWarning;
            threatLabel.color = state.DefconLevel <= 2 ? AvTheme.Alert
                              : state.DefconLevel == 3 ? AvTheme.Warning
                              : AvTheme.Dim;

            bool airKnown = !float.IsNaN(state.AirSuperiorityRatio);
            airCountLabel.text = "ALLIED " + state.FriendlyAircraftCount +
                                 "   ·   HOSTILE " + state.HostileAircraftCount +
                                 "   ·   " + TheaterReadout.Percent(state.AirSuperiorityRatio) + " DOMINANCE";
            if (airBarRoot != null && airBarRoot.activeSelf != airKnown) airBarRoot.SetActive(airKnown);
            if (airKnown)
            {
                airFill.fillAmount = Mathf.Clamp01(state.AirSuperiorityRatio);
                airFill.color = state.AirSuperiorityRatio >= 0.5f ? AvTheme.RailReady : AvTheme.RailCaution;
            }

            SortieTally tally = state.Sorties;
            bool known = tally.Observed > 0;

            sortieNote.text = known
                ? tally.Observed + " AI AIRCRAFT OBSERVED · " + tally.Tasked + " TASKED"
                : "NO AI PILOT STATE AVAILABLE — SORTIE ROLES UNKNOWN";
            sortieNote.color = known ? AvTheme.Dim : AvTheme.RailCaution;

            // The track under each count is the role's share of the aircraft the scan
            // actually saw, so every bar has a figure above it and a denominator the note
            // states.
            int observed = Mathf.Max(1, tally.Observed);
            for (int i = 0; i < Roles.Length; i++)
            {
                int count = tally.Of(Roles[i]);
                sortieRows[i].Bind(
                    SortieClassifier.Code(Roles[i]) + " · " + SortieClassifier.Name(Roles[i]),
                    known ? count.ToString() : "—",
                    known,
                    count / (float)observed,
                    Roles[i] == SortieRole.Transit ? AvTheme.RailInert : AvTheme.RailInfo);
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

            controlBarRect = new Rect(x, y, width, 8f);
            AvStyled.Box(parent, controlBarRect, "bar");
            for (int i = 0; i < controlBarFill.Length; i++)
            {
                controlBarFill[i] = AvKit.Panel(parent, new Rect(x, y, 0f, 8f), Color.clear);
            }
            y -= 12f;

            for (int i = 0; i < sectorLegend.Length; i++)
            {
                sectorLegend[i] = new SectorLegend(parent, x, y - i * LegendPitch, width);
            }
            y -= sectorLegend.Length * LegendPitch + 6f;

            frontlineValue = KeyValue(parent, x, y, width, "FRONTLINE LENGTH");
            y -= KvPitch;
            nodeValue = KeyValue(parent, x, y, width, "TRACKED NODES");
            y -= KvPitch + 4f;

            y = SectionHeader(parent, x, y, width, "CONTESTED GROUND", "BY PRESSURE", band: true);

            nodeNote = AvStyled.Label(parent, new Rect(x, y, width, 14f), "", "row-sub");
            y -= 16f + 2f;

            for (int i = 0; i < nodeRowsBuilt; i++)
            {
                nodeRows[i] = new ListRow(parent, x, y - i * nodeRowPitch, width, nodeRowPitch);
            }
        }

        private void RefreshFrontBody(TacticalTheaterState state)
        {
            if (frontlineValue == null) return;

            TheaterReadout.Shares(
                state.FriendlySectorCount, state.ContestedSectorCount,
                state.HostileSectorCount, state.NeutralSectorCount,
                out float friendly, out float contested, out float hostile);

            float[] shares = { friendly, contested, hostile };
            Color[] colours = {
                AvStyleHost.Resolve(AvStyleHost.Style("bar-friendly").Background, AvTheme.Accent),
                AvStyleHost.Resolve(AvStyleHost.Style("bar-contested").Background, AvTheme.RailCaution),
                AvStyleHost.Resolve(AvStyleHost.Style("bar-hostile").Background, AvTheme.RailDanger)
            };

            float cursor = controlBarRect.x;
            for (int i = 0; i < controlBarFill.Length; i++)
            {
                float w = Mathf.Max(0f, controlBarRect.width * shares[i]);
                AvKit.Place(controlBarFill[i].rectTransform,
                            new Rect(cursor, controlBarRect.y, w, controlBarRect.height));
                controlBarFill[i].color = w > 0.5f ? colours[i] : Color.clear;
                cursor += w;
            }

            // The bar shows the split; the legend rows put the same split into figures on
            // one column grid, so the counts and shares line up instead of riding the end
            // of a sentence.
            float unclaimed = Mathf.Clamp01(1f - friendly - contested - hostile);
            sectorLegend[0].Bind("ALLIED", state.FriendlySectorCount.ToString(),
                                 TheaterReadout.Percent(friendly), colours[0], AvTheme.TextPrimary);
            sectorLegend[1].Bind("CONTESTED", state.ContestedSectorCount.ToString(),
                                 TheaterReadout.Percent(contested), colours[1],
                                 state.ContestedSectorCount > 0 ? AvTheme.RailCaution : AvTheme.TextPrimary);
            sectorLegend[2].Bind("HOSTILE", state.HostileSectorCount.ToString(),
                                 TheaterReadout.Percent(hostile), colours[2], AvTheme.TextPrimary);
            sectorLegend[3].Bind("UNCLAIMED", state.NeutralSectorCount.ToString(),
                                 TheaterReadout.Percent(unclaimed), AvTheme.RailInert, AvTheme.Dim);

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
                for (int i = 0; i < nodeRowsBuilt; i++) nodeRows[i].Hide();
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
                if (slot >= nodeRowsBuilt) continue;

                for (int j = Mathf.Min(shown, nodeRowsBuilt - 1); j > slot; j--)
                {
                    ranked[j] = ranked[j - 1];
                }

                ranked[slot] = nodes[i];
                if (shown < nodeRowsBuilt) shown++;
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
                string name = string.IsNullOrEmpty(node.Name) ? "UNNAMED NODE" : node.Name.ToUpperInvariant();
                string state = TheaterReadout.PressureState(node.CaptureProgress);
                string figure = pressing ? TheaterReadout.Percent(node.CaptureProgress) : "—";

                // A row that carries no percentage says why in words beside the dash, and
                // the track stays off: no bare bar is drawn for a reading that is not there.
                nodeRows[i].Bind(
                    TheaterReadout.NodeRail(friendly, node.IsContested),
                    name,
                    (node.IsAirbase ? "AIRBASE" : "STRONGPOINT") + " · " +
                    (friendly ? "ALLIED HELD" : "HOSTILE HELD") + " · " + state,
                    figure,
                    pressing,
                    pressing ? node.CaptureProgress : 0f,
                    tint, tint,
                    null,
                    name + " — " + (friendly ? "allied held" : "hostile held") + ", " + state.ToLowerInvariant() + ".");
            }

            for (int i = shown; i < nodeRowsBuilt; i++) nodeRows[i].Hide();

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
            theaterOperations?.Refresh();

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            FactionHQ hq = map != null ? map.HQ : null;
            if (hq != null) command.UpdateTelemetry(hq);

            TacticalTheaterState state = command.TheaterState;

            RefreshChrome(state);

            // The map ring follows the open file, which lives on the COC page: any other page
            // showing means no file is open, so nothing is ringed.
            if (shell.Page != TabCoc && highCommand != null) highCommand.Highlight(-1);

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
            shell.DataBar.State.text = state.PrimaryThreatDescription == "ACTIVE GROUND BATTLE"
                ? "GROUND BATTLE" : state.PrimaryThreatDescription;
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
                string offensive = OffensiveAmbient();
                text += " · " + (offensive ?? (theaterPriority == null || !theaterPriority.Available
                    ? "theater operations not running"
                    : theaterPriority.HasPriority
                        ? "main effort " + theaterPriority.PriorityLabel
                        : "staff holds no main effort"));
            }
            return text;
        }

        /// <summary>
        /// The one-line offensive report for the pinned strip, or null when the board has
        /// nothing of its own to add. The strip is where a running offensive can be watched
        /// without the page being open, since it ticks on the host whether or not anyone looks.
        /// </summary>
        private string OffensiveAmbient()
        {
            if (theaterOperations == null || !theaterOperations.Available) return null;

            IReadOnlyList<TheaterOperationView> operations = theaterOperations.Operations;
            for (int i = 0; i < operations.Count; i++)
            {
                TheaterOperationView operation = operations[i];
                if (operation == null || operation.Phase == TheaterOperationPhase.Concluded) continue;

                string target = string.IsNullOrEmpty(operation.TargetLabel)
                    ? ""
                    : " at " + operation.TargetLabel;

                switch (operation.Phase)
                {
                    case TheaterOperationPhase.AwaitingTarget:
                        return operation.Name + " awaits a target";
                    case TheaterOperationPhase.Holding:
                        return operation.Name + " is holding for reinforcement" + target;
                    case TheaterOperationPhase.Launching when operation.IsHeld:
                        return operation.Name + " is held — " + operation.Holder + " has the effort";
                    default:
                        return operation.Name + " " +
                               TheaterReadout.OffensivePhaseWord(operation.Phase, operation.Outcome)
                                   .ToLowerInvariant() + target;
                }
            }
            return null;
        }
    }
}
