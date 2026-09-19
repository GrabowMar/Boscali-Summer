using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Progression.Configuration;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// "SQD" — pilot status, shared skill board with support authorisations, enemy ace
    /// roster, and the local pilot studio / squadron identity page. Reads encounter
    /// snapshots only; Wing Command owns aircraft orders, custom-pilot files and pilot
    /// generation. Emblems and the local pilot profile are client-local cosmetics.
    /// </summary>
    internal sealed partial class SqdMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.20f;
        private const float SpineInset = 14f;

        /// <summary>
        /// The vertical rhythm the four pages share: the gap between two blocks at rest,
        /// and the ceiling it may grow to when the bezel hands back spare glass. A page
        /// never opens a band wider than the ceiling — that is what used to read as a
        /// hole above the status strip.
        /// </summary>
        private const float SheetBaseGap = 10f;
        private const float SheetGapMax = 22f;

        private const float SheetHeaderHeight = 50f;
        private const float SheetHeadingHeight = 24f;

        private const int ChipCount = 3;

        private const int TabPilot = 0;
        private const int TabSkills = 1;
        private const int TabWings = 2;
        private const int TabStudio = 3;

        private const int MaximumBudgetPips = 20;
        private const int WingRowsPerPage = 2;

        /// <summary>What each operational page is called, in tab order.</summary>
        private static readonly string[] SheetNames =
        {
            "PILOT STATUS", "QUALIFICATIONS", "WING STATUS", "PILOT STUDIO",
        };

        private ProgressionManager progression;
        private ProgressionSettings settings;
        private ISquadView squad;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private TMP_FontAsset font;
        private AvScreen shell;

        private AvStyled.DataBar dataBar;
        private AvStyled.Metric scoreMetric;
        private AvStyled.Metric budgetMetric;

        // ---- Cockpit cosmetics (client-local) ---------------------------------------------

        private EmblemDesign emblem = EmblemDesign.Default;
        private string squadronName = string.Empty;
        private Sprite emblemSprite;
        private string emblemSpriteKey;
        private string[] emblemFiles = Array.Empty<string>();
        private int emblemFileIndex = -1;
        private string cosmeticNameText;
        private string cosmeticEmblemText;
        private string cosmeticFileText;

        private WingPilotRecord localProfile;
        private bool hasLocalProfile;
        private string localProfileKey;
        private Sprite profilePortrait;
        private string profilePortraitKey;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(ProgressionManager manager, ISquadView squadView,
            ProgressionSettings progressionSettings, ManualLogSource log)
        {
            progression = manager;
            squad = squadView;
            settings = progressionSettings;
            logger = log;
            emblem = EmblemDesign.Parse(settings?.Emblem?.Value, EmblemDesign.Default);
            squadronName = settings?.SquadronName?.Value ?? "BOSCALI SUMMER";
        }

        public void ResetForScene()
        {
            AvKit.ReleaseKeyboardGuard();
            MfdBezel.Release(MfdSlots.Sqd);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            font = null;
            shell = null;
            dataBar = null;
            scoreMetric = null;
            budgetMetric = null;

            ResetSkillRows();
            ResetPilotPage();
            ResetWingsPage();
            ResetStudioPage();

            emblemSprite = null;
            emblemSpriteKey = null;
            emblemFiles = Array.Empty<string>();
            emblemFileIndex = -1;
            cosmeticNameText = null;
            cosmeticEmblemText = null;
            cosmeticFileText = null;
            profilePortrait = null;
            profilePortraitKey = null;
            hasLocalProfile = false;
            localProfileKey = null;

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            SetViewOpen(false);
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || progression == null) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            bool visible = screen.isActive && SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            SetViewOpen(visible);
            if (visible && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
        }

        private void SetViewOpen(bool open)
        {
            if (viewOpen == open) return;
            viewOpen = open;
            if (!open) AvKit.ReleaseKeyboardGuard();
            ((IProgressionView)progression)?.SetViewOpen(open);
        }

        private IProgressionView Progress => progression;

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Sqd, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger.LogWarning("SQD MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null) { MfdBezel.Release(MfdSlots.Sqd); return; }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Sqd);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    ResetForScene();
                    failed = true;
                    logger.LogWarning("SQD MFD unavailable: bezel changed during installation.");
                    return;
                }
                logger.LogInfo("SQD MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                ResetForScene();
                failed = true;
                logger.LogError("SQD MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliSquadron.Screen", typeof(RectTransform), typeof(Image));
            screenRoot = root;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvScreen.ResolveHeight(
                templateRect.parent as RectTransform, PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvKit.ClampIntoCanvas(rootRect);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            shell = AvScreen.Build(
                content, "SQD",
                new[] { "01 PILOT", "02 SKILLS", "03 WINGS", "04 STUDIO" },
                new[]
                {
                    new[] { "PILOT SCORE", "THIS PILOT" },
                    new[] { "QUAL. PICKS", "UNSPENT" },
                },
                ChipCount, Width, height, OnTabChanged);

            dataBar = shell.DataBar;
            scoreMetric = shell.Metrics[0];
            budgetMetric = shell.Metrics[1];
            PrepareShell();

            Rect body = shell.Body;

            BuildPilotPage((RectTransform)shell.CreatePage(TabPilot, "PilotPage").transform, body);
            BuildSkillsPage((RectTransform)shell.CreatePage(TabSkills, "SkillsPage").transform, body);
            BuildWingsPage((RectTransform)shell.CreatePage(TabWings, "WingsPage").transform, body);
            BuildStudioPage((RectTransform)shell.CreatePage(TabStudio, "StudioPage").transform, body);

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Sqd;
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
            shell.SetPage(TabPilot);
            return result;
        }

        private void OnTabChanged(int page)
        {
            CancelSkillConfirmation();
            nextRefresh = 0f;
            if (page == TabStudio)
            {
                studioDirty = true;
                studioArtDirty = true;
            }
        }

        /// <summary>
        /// The shell builds the metric row and the tabs; their copy is the panel's. The
        /// metric units and captions are single-line readouts at a tracked font, so they
        /// shrink to the micro floor rather than clipping a figure into an ellipsis, and
        /// every tab gets the tooltip the other controls already carry.
        /// </summary>
        private void PrepareShell()
        {
            PrepareMetric(scoreMetric);
            PrepareMetric(budgetMetric);

            string[] hints =
            {
                "Pilot status, current sortie, service background and career totals.",
                "Compare qualification grades and spend an available pick.",
                "Review your recruited wing and known hostile ace wings.",
                "Manage local pilot profiles and squadron identity.",
            };
            if (shell?.Tabs == null) return;
            for (int i = 0; i < shell.Tabs.Length && i < hints.Length; i++)
                shell.Tabs[i].WithTooltip(hints[i]);
        }

        private static void PrepareMetric(AvStyled.Metric metric)
        {
            if (metric == null) return;
            FitSingleLine(metric.Unit);
            FitSingleLine(metric.Caption);
        }

        /// <summary>
        /// A readout that must never end in "...": auto-size down to the micro floor and
        /// let the label overflow its box rather than trade a figure for an ellipsis.
        /// </summary>
        private static void FitSingleLine(TMP_Text label)
        {
            if (label == null) return;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = true;
            label.fontSizeMin = AvTokens.FontMicro;
            label.fontSizeMax = label.fontSize;
        }

        /// <summary>
        /// The gap between two page blocks once the bezel's spare height is shared out.
        /// Calling this the same way on every page keeps the four pages on one rhythm.
        /// </summary>
        private static float SheetGap(float bodyHeight, float contentHeight, int gapCount)
        {
            float slack = bodyHeight - contentHeight;
            if (slack <= 0f || gapCount <= 0) return SheetBaseGap;
            return SheetBaseGap + Mathf.Min(slack * 0.6f / gapCount, SheetGapMax - SheetBaseGap);
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (scoreMetric == null || progression == null) return;

            bool bypass = progression.BypassRequirements;
            int score = Progress.Score;
            int bonus = 0;
            if (squad != null && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                ulong id = PlayerIdentity.Of(local);
                score = Math.Max(0, score - squad.GetScoreOrigin(id));
                bonus = squad.GetBonusPoints(id);
            }

            RefreshCosmetics();
            RefreshDataBar(bypass);
            RefreshMetrics(bypass, score, bonus);

            switch (shell.Page)
            {
                case TabPilot: RefreshPilotPage(bypass, score, bonus); break;
                case TabSkills: RefreshSkillsPage(); break;
                case TabWings: RefreshWingsPage(); break;
                case TabStudio: RefreshStudioPage(); break;
            }

            UpdateStatusStrip();
        }

        private void RefreshDataBar(bool bypass)
        {
            bool hunted = squad != null && squad.HuntActive;
            dataBar.State.text = hunted ? "ACE HUNT ACTIVE" : bypass
                ? "DEBUG BYPASS" : "PERSONNEL FILE";
            dataBar.State.color = hunted ? AvTheme.Alert : bypass ? AvTheme.Warning : AvTheme.RailReady;
            dataBar.SetChip(0, hunted ? "HUNT ACTIVE" : "NO HUNT", hunted);
            dataBar.SetChip(1, "RANK " + Progress.Rank, true);
            int available = Progress.AvailablePoints;
            dataBar.SetChip(2, bypass ? "ALL OPEN" : available + " PICK" + (available == 1 ? "" : "S"),
                available > 0 || bypass);
        }

        private void RefreshMetrics(bool bypass, int score, int bonus)
        {
            IProgressionView view = Progress;
            int ceiling = Mathf.Max(1, view.MaximumPoints);
            int available = view.AvailablePoints;
            bool capped = view.EarnedPoints >= ceiling;
            // The same ramp the host pays out, so the header never divides score itself.
            int remaining = PerkPoints.RemainingToNext(score, view.ScorePerPoint);

            scoreMetric.Set(
                bypass ? "BYPASS" : score.ToString("N0"),
                bypass ? "EVERY GRADE OPEN" : capped ? "CEILING REACHED"
                    : remaining < 0 ? "LADDER COMPLETE"
                    : "NEXT IN " + remaining,
                bypass || capped || remaining < 0 ? 1f
                    : 1f - remaining / (float)Mathf.Max(1, view.ScorePerPoint * Mathf.Max(1, PerkCatalog.MaximumDepth)),
                bypass ? AvTheme.Warning : AvTheme.RailReady);
            scoreMetric.Unit.text = "THIS PILOT";

            int earned = view.EarnedPoints;
            budgetMetric.Set(
                bypass ? "FREE" : available + " PICK" + (available == 1 ? "" : "S"),
                bypass ? "UNLIMITED PICKS" : $"{earned}/{ceiling} EARNED · +{bonus}",
                bypass ? 1f : earned / (float)ceiling,
                available > 0 ? AvTheme.RailReady : AvTheme.RailInfo);
            budgetMetric.Unit.text = "UNSPENT";
        }

        private void UpdateStatusStrip()
        {
            if (shell == null) return;
            string baseLine = shell.Page == TabWings
                ? (squad != null ? squad.Status : "Enemy wing reports are unavailable.")
                : shell.Page == TabStudio ? StudioStatusLine()
                : progression.BypassRequirements ? "DEBUG BYPASS — EVERY GRADE OPEN"
                : progression.LastResult;

            string pageTag = shell.Page >= 0 && shell.Page < SheetNames.Length
                ? SheetNames[shell.Page] + "  ·  "
                : string.Empty;
            shell.WriteStatus(null, MapPicker.Prompt, pageTag + baseLine);
        }

        // ---- Shared drawing helpers ------------------------------------------------------

        /// <summary>A quiet instrument section: accent cue, title, optional context and rule.</summary>
        private float DrawSectionTitle(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
            => DrawSectionTitle(parent, x, y, width, title, note, band, out _);

        private float DrawSectionTitle(
            RectTransform parent, float x, float y, float width, string title, string note, bool band,
            out TMP_Text noteLabel)
        {
            noteLabel = null;
            AvKit.Rule(parent, new Rect(x, y - 1f, 3f, 14f),
                band ? AvTheme.RailInfo : AvTheme.Accent.WithAlpha(0.75f));
            TMP_Text heading = PlainLabel(parent, new Rect(x + 10f, y, width - 10f, 14f),
                title, "section-title");

            if (!string.IsNullOrEmpty(note))
            {
                noteLabel = PlainLabel(parent, new Rect(x + width * 0.46f, y,
                    width * 0.54f, 14f), note, "section-title-note");
                noteLabel.alignment = TextAlignmentOptions.MidlineRight;
            }

            AvKit.Rule(parent, new Rect(x, y - 18f, width, 1f), AvTheme.Hairline.WithAlpha(0.34f));
            return y - 24f;
        }

        /// <summary>Page identity with a readable title and one restrained divider.</summary>
        private static float DrawPageHeader(
            RectTransform parent, float x, float y, float width, string eyebrowText, string title, string meta)
        {
            TMP_Text eyebrow = PlainLabel(parent, new Rect(x, y, width, 12f), eyebrowText, "section-title-note");
            eyebrow.color = AvTheme.RailInfo;
            PlainLabel(parent, new Rect(x, y - 15f, width, 22f), title, "page-title");
            if (!string.IsNullOrEmpty(meta))
            {
                TMP_Text note = PlainLabel(parent, new Rect(x + width * 0.52f, y - 16f,
                    width * 0.48f, 20f), meta, "section-title-note");
                note.alignment = TextAlignmentOptions.MidlineRight;
            }

            AvKit.Rule(parent, new Rect(x, y - 40f, width, 2f), AvTheme.RailInfo.WithAlpha(0.65f));
            return y - 50f;
        }

        /// <summary>A flat, word-labelled status badge.</summary>
        private static (GameObject Root, Image Fill, TMP_Text Label) StatusBadge(
            RectTransform parent, Rect area, string text, string state)
        {
            var go = new GameObject("StatusBadge", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, area);
            Image fill = AvKit.Panel(rect, new Rect(0f, 0f, area.width, area.height), AvTheme.SurfaceInert);
            AvKit.Outline(rect, new Rect(0f, 0f, area.width, area.height), StateColour(state).WithAlpha(0.7f));
            TMP_Text label = PlainLabel(rect, new Rect(0f, 0f, area.width, area.height), text, "row-sub");
            label.color = StateColour(state);
            label.alignment = TextAlignmentOptions.Center;
            return (go, fill, label);
        }

        /// <summary>Restate a status badge without rebuilding its frame.</summary>
        private static void PaintStatusBadge(Image fill, TMP_Text label, string state, string text)
        {
            label.text = text;
            label.color = StateColour(state);
            if (fill != null) fill.color = StateColour(state).WithAlpha(0.08f);
        }

        private static Color StateColour(string state) => state == "bad" ? AvTheme.RailDanger
            : state == "warn" ? AvTheme.RailCaution
            : state == "ok" ? AvTheme.RailReady
            : AvTheme.RailInfo;

        /// <summary>An explicit visual-unavailable state used by every portrait frame.</summary>
        private static GameObject VisualPlaceholder(RectTransform parent, Rect area)
        {
            var block = new GameObject("VisualPlaceholder", typeof(RectTransform));
            var rect = (RectTransform)block.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, area);

            AvKit.Panel(rect, new Rect(0f, 0f, area.width, area.height), AvTheme.SurfaceInert);
            AvKit.Rule(rect, new Rect(0f, 0f, 3f, area.height), AvTheme.RailInert);
            float labelPad = area.width < 60f ? 2f : 6f;
            TMP_Text label = PlainLabel(rect,
                new Rect(labelPad, 0f, Mathf.Max(0f, area.width - labelPad * 2f), area.height),
                area.width < 60f ? "NO\nPHOTO" : "NO VISUAL", "row-sub");
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = AvTokens.FontMicro;
            label.fontSizeMax = label.fontSize;
            return block;
        }

        /// <summary>A single structural rail shared by all SQD pages.</summary>
        private static void PageRail(RectTransform parent, Rect body)
        {
            Image rail = AvKit.Rule(parent, new Rect(body.x, body.y, 2f, body.height),
                AvTheme.RailInfo.WithAlpha(0.45f));
            rail.raycastTarget = false;
        }

        private static void RowSeparator(RectTransform parent, Rect area) =>
            AvKit.Rule(parent, new Rect(area.x, area.y - area.height, area.width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        /// <summary>
        /// One aligned status field: key at left and value at right. The value shrinks to
        /// the micro floor and overflows its own box rather than ending in an ellipsis.
        /// Built once per field and re-bound on refresh.
        /// </summary>
        private sealed class FormRow
        {
            private readonly TMP_Text key;
            public readonly TMP_Text Value;

            public FormRow(RectTransform parent, float lineHeight)
            {
                key = PlainLabel(parent, new Rect(0f, 0f, 10f, lineHeight), "", "form-key");
                Value = PlainLabel(parent, new Rect(0f, 0f, 10f, lineHeight), "—", "form-value");
                Value.alignment = TextAlignmentOptions.MidlineRight;
                Value.enableAutoSizing = true;
                Value.fontSizeMin = AvTokens.FontMicro;
                Value.fontSizeMax = Value.fontSize;
                Value.overflowMode = TextOverflowModes.Overflow;
                key.enableAutoSizing = true;
                key.fontSizeMin = AvTokens.FontMicro;
                key.fontSizeMax = key.fontSize;
                key.overflowMode = TextOverflowModes.Overflow;
            }

            /// <summary>Lay the field at y; returns the y the next field uses.</summary>
            public float Bind(float x, float y, float width, float pitch, string keyText, string valueText)
            {
                float line = Mathf.Max(12f, pitch - 2f);
                key.text = keyText ?? "";
                float keyWidth = width * 0.46f;
                AvKit.Place(key.rectTransform, new Rect(x, y, keyWidth, line));

                Value.text = string.IsNullOrEmpty(valueText) ? "—" : valueText;
                float valueWidth = Mathf.Max(44f, width - keyWidth - 8f);
                AvKit.Place(Value.rectTransform, new Rect(x + width - valueWidth, y, valueWidth, line));
                return y - pitch;
            }
        }

        /// <summary>
        /// A roster cell that carries a name off the wire: shrink to the micro floor
        /// rather than cut a pilot's identity into an ellipsis.
        /// </summary>
        private static TMP_Text Fitted(TMP_Text label)
        {
            if (label == null) return null;
            label.enableAutoSizing = true;
            label.fontSizeMin = AvTokens.FontMicro;
            label.fontSizeMax = label.fontSize;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        /// <summary>A field row with its cursor advanced, for a straight run of fields.</summary>
        private static TMP_Text Field(
            RectTransform parent, float pitch, float x, ref float y, float width, string key)
        {
            var row = new FormRow(parent, pitch - 2f);
            y = row.Bind(x, y, width, pitch, key, "—");
            return row.Value;
        }

        private static Color RailColour(string state) =>
            AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);

        private static Color HoverFill() =>
            AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised);

        /// <summary>Small vector hunt/ace mark, drawn into an area without allocating art.</summary>
        private static void Glyph(RectTransform parent, Rect area, HuntMark mark, Color color)
        {
            var go = new GameObject(mark.ToString(), typeof(RectTransform), typeof(CanvasRenderer), typeof(HuntGlyph));
            go.transform.SetParent(parent, false);
            AvKit.Place((RectTransform)go.transform, area);
            HuntGlyph glyph = go.GetComponent<HuntGlyph>();
            glyph.Mark = mark;
            glyph.color = color;
            glyph.raycastTarget = false;
        }

        private static TMP_Text PlainLabel(RectTransform parent, Rect area, string text, string classes)
        {
            TMP_Text label = AvStyled.Label(parent, area, text, classes, align: TextAlignmentOptions.MidlineLeft);
            label.richText = false;
            return label;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        // ---- Local cosmetics -------------------------------------------------------------

        private void RefreshCosmetics()
        {
            string wantedName = settings?.SquadronName?.Value ?? string.Empty;
            if (!string.Equals(cosmeticNameText, wantedName, StringComparison.Ordinal))
            {
                cosmeticNameText = wantedName;
                if (wantedName.Length > 24) wantedName = wantedName.Substring(0, 24);
                squadronName = wantedName;
            }

            string wantedEmblem = settings?.Emblem?.Value ?? string.Empty;
            if (!string.Equals(cosmeticEmblemText, wantedEmblem, StringComparison.Ordinal))
            {
                cosmeticEmblemText = wantedEmblem;
                EmblemDesign wanted = EmblemDesign.Parse(wantedEmblem, EmblemDesign.Default);
                if (wanted != emblem)
                {
                    emblem = wanted;
                    emblemSprite = null;
                }
            }

            string wantedFile = settings?.EmblemFile?.Value ?? string.Empty;
            if (!string.Equals(cosmeticFileText, wantedFile, StringComparison.Ordinal))
            {
                cosmeticFileText = wantedFile;
                emblemSprite = null;
            }
            if (emblemFileIndex < 0 || emblemFileIndex >= emblemFiles.Length ||
                !string.Equals(emblemFiles[emblemFileIndex], wantedFile, StringComparison.OrdinalIgnoreCase))
            {
                emblemFileIndex = -1;
                for (int i = 0; i < emblemFiles.Length; i++)
                    if (string.Equals(emblemFiles[i], wantedFile, StringComparison.OrdinalIgnoreCase))
                    {
                        emblemFileIndex = i;
                        break;
                    }
            }
            string key = emblem.Encode() + "|" + wantedFile;
            if (emblemSpriteKey != key || emblemSprite == null)
            {
                emblemSpriteKey = key;
                emblemSprite = string.IsNullOrEmpty(wantedFile)
                    ? EmblemRenderer.Procedural(emblem)
                    : EmblemRenderer.File(wantedFile) ?? EmblemRenderer.Procedural(emblem);
            }

            string profileKey = settings?.PilotProfile?.Value ?? string.Empty;
            if (!string.Equals(localProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
            {
                localProfileKey = profileKey;
                hasLocalProfile = !string.IsNullOrEmpty(profileKey) &&
                    WingLink.TryGetCustomPilot(profileKey, out localProfile);
                profilePortrait = null;
                profilePortraitKey = null;
            }
        }

        private Sprite PlayerPortrait(string name, string callsign)
        {
            if (hasLocalProfile && localProfile.HasPortrait)
            {
                string key = "p|" + localProfile.Body + "|" + localProfile.Face + "|" + localProfile.Hair + "|" +
                    localProfile.Uniform + "|" + localProfile.Accessory + "|" + localProfile.Backdrop;
                if (profilePortraitKey != key)
                {
                    profilePortraitKey = key;
                    profilePortrait = WingLink.PilotPortraitForSelection(localProfile.Body,
                        localProfile.Face, localProfile.Hair, localProfile.Uniform,
                        localProfile.Accessory, localProfile.Backdrop);
                }
                if (profilePortrait != null) return profilePortrait;
            }
            // The identity portrait goes through the same cache: Wing Command's companion API
            // is a reflection seam, and a refresh must not re-enter it for an unchanged face.
            string identityKey = "i|" + name + "|" + callsign;
            if (profilePortraitKey != identityKey)
            {
                profilePortraitKey = identityKey;
                profilePortrait = WingLink.PilotPortrait(name, callsign);
            }
            return profilePortrait;
        }

        private static void SetPortrait(Image image, GameObject fallback, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
            if (fallback != null) fallback.SetActive(sprite == null);
        }

        // ---- Row pooling -----------------------------------------------------------------

        private sealed class SkillRow
        {
            public byte Id;
            public AvButton Select;
            public Image Fill;
            public Image Hover;
            public Image[] Frame;
            public Image Rail;
            public SqdGlyph Icon;
            public TMP_Text Name;
            public TMP_Text State;
        }

        /// <summary>One lane's caption and committed count, refreshed as grades are taken.</summary>
        private sealed class SkillBranchRow
        {
            public string Name;
            public TMP_Text Caption;
            public TMP_Text Note;
            public readonly List<byte> Ids = new List<byte>(PerkCatalog.MaximumDepth);
        }

        private sealed class WingRow
        {
            public RectTransform Root;
            public GameObject Empty;
            public TMP_Text EmptyLabel;
            public Image Rail;
            public Image Crest;
            public string CrestKey;
            public Image Portrait;
            public GameObject PortraitFallback;
            public TMP_Text Symbol;
            public TMP_Text Wing;
            public TMP_Text Ace;
            public TMP_Text Skill;
            public TMP_Text Status;
            public TMP_Text Members;
            public TMP_Text Target;
            public TMP_Text NoSkills;
            public readonly GameObject[] Badges = new GameObject[AceSkillCatalog.MaximumSkills];
        }
    }
}
