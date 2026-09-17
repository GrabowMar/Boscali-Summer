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
    /// "SQD" — pilot dossier, shared skill board with support authorisations, enemy ace
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

        private const int ChipCount = 3;

        private const int TabPilot = 0;
        private const int TabSkills = 1;
        private const int TabWings = 2;
        private const int TabStudio = 3;

        private const int MaximumBudgetPips = 20;
        private const int WingRowsPerPage = 2;

        /// <summary>What each sheet of the SQD file is called, in tab order.</summary>
        private static readonly string[] SheetNames =
        {
            "PERSONNEL FILE", "QUALIFICATION RECORD", "ORDER OF BATTLE", "STUDIO RECORD",
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

        /// <summary>Clause number, restarted by every page build so the headings read 01, 02, 03.</summary>
        private int clause;

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
                    new[] { "PILOT SCORE", "SCORE · THIS PILOT" },
                    new[] { "QUALIFICATION PICKS", "UNSPENT" },
                },
                ChipCount, Width, height, OnTabChanged);

            dataBar = shell.DataBar;
            scoreMetric = shell.Metrics[0];
            budgetMetric = shell.Metrics[1];

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
            dataBar.State.text = hunted ? "ACE WING HUNTING YOU" : bypass
                ? "DEBUG BYPASS — EVERY GRADE OPEN" : "PERSONNEL FILE · PILOT & QUALIFICATION RECORD";
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
                bypass ? "EVERY GRADE OPEN" : capped ? "PICK CEILING REACHED"
                    : remaining < 0 ? "GRADE LADDER COMPLETE"
                    : remaining + " SCORE TO NEXT GRADE",
                bypass || capped || remaining < 0 ? 1f
                    : 1f - remaining / (float)Mathf.Max(1, view.ScorePerPoint * Mathf.Max(1, PerkCatalog.MaximumDepth)),
                bypass ? AvTheme.Warning : AvTheme.RailReady);
            scoreMetric.Unit.text = "SCORE · THIS PILOT";

            int earned = view.EarnedPoints;
            budgetMetric.Set(
                bypass ? "FREE" : available + " PICK" + (available == 1 ? "" : "S"),
                bypass ? "UNLIMITED PICKS" : $"{earned}/{ceiling} EARNED · {bonus} ACE BONUS",
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

            string fileTag = shell.Page >= 0 && shell.Page < SheetNames.Length
                ? "FILE 0" + (shell.Page + 1) + "/04 · " + SheetNames[shell.Page] + " · "
                : string.Empty;
            shell.WriteStatus(null, MapPicker.Prompt, fileTag + baseLine);
        }

        // ---- Shared drawing helpers ------------------------------------------------------

        /// <summary>
        /// A numbered clause heading: "03 · SERVICE BACKGROUND .......... PILOT LORE".
        /// The counter runs per page build, so the pages read as sections of one file.
        /// </summary>
        private float DrawSectionTitle(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
            => DrawSectionTitle(parent, x, y, width, title, note, band, out _);

        private float DrawSectionTitle(
            RectTransform parent, float x, float y, float width, string title, string note, bool band,
            out TMP_Text noteLabel)
        {
            noteLabel = null;
            if (band) AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 6f, 22f), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 7f);

            string clauseText = (++clause).ToString("00") + " · " + title;
            TMP_Text heading = PlainLabel(parent, new Rect(x, y, width, 14f), clauseText, "section-title");
            float headingWidth = Mathf.Ceil(heading.GetPreferredValues(clauseText).x);

            if (!string.IsNullOrEmpty(note))
            {
                noteLabel = PlainLabel(parent, new Rect(x, y, width, 14f), note,
                                                "section-title-note");
                noteLabel.alignment = TextAlignmentOptions.MidlineRight;
                float noteWidth = Mathf.Ceil(noteLabel.GetPreferredValues(note).x);
                DottedLeader(parent, x + headingWidth + 6f, y,
                    width - headingWidth - noteWidth - 12f);
            }

            AvKit.Rule(parent, new Rect(x, y - 15f, width, 1f), AvTheme.Hairline.WithAlpha(0.16f));
            return y - 24f;
        }

        /// <summary>
        /// The file masthead: form number over the document title, the file's own note on
        /// the right, and the double rule a service form is filed under.
        /// </summary>
        private static float DrawFileHeader(
            RectTransform parent, float x, float y, float width, string form, string title, string meta)
        {
            PlainLabel(parent, new Rect(x, y, width, 12f), form, "file-form");
            PlainLabel(parent, new Rect(x, y - 13f, width, 20f), title, "file-title");
            if (!string.IsNullOrEmpty(meta))
            {
                TMP_Text note = PlainLabel(parent, new Rect(x, y - 13f, width, 20f), meta, "file-meta");
                note.alignment = TextAlignmentOptions.MidlineRight;
            }

            AvKit.Rule(parent, new Rect(x, y - 37f, width, 1f), AvTheme.Frame.WithAlpha(0.55f));
            AvKit.Rule(parent, new Rect(x, y - 40f, width, 2f), AvTheme.RailInfo.WithAlpha(0.30f));
            return y - 50f;
        }

        /// <summary>Dots filling the gap between a label and what sits to its right.</summary>
        private static void DottedLeader(RectTransform parent, float x, float y, float width)
        {
            if (width < 8f) return;

            TMP_Text leader = PlainLabel(parent, new Rect(x, y, width, 14f), ".", "leader");
            float dot = leader.GetPreferredValues(".").x;
            if (dot <= 0.5f) return;

            leader.text = new string('.', Mathf.Max(1, Mathf.FloorToInt(width / dot) - 1));
        }

        /// <summary>A rotated rubber stamp, the way a file gets marked on receipt.</summary>
        private static (GameObject Root, Image Fill, TMP_Text Label) Stamp(
            RectTransform parent, Rect area, string text, string state)
        {
            var go = new GameObject("Stamp", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localRotation = Quaternion.Euler(0f, 0f, -6f);
            AvKit.Place(rect, area);

            Image fill = AvStyled.Box(rect, new Rect(0f, 0f, area.width, area.height), "stamp " + state);
            TMP_Text label = PlainLabel(rect, new Rect(0f, 0f, area.width, area.height), text,
                                        "stamp " + state);
            label.alignment = TextAlignmentOptions.Center;
            return (go, fill, label);
        }

        /// <summary>Restate a stamp's mark without rebuilding its frame.</summary>
        private static void PaintStamp(Image fill, TMP_Text label, string state, string text)
        {
            AvStyle style = AvStyleHost.Style("stamp " + state);
            label.text = text;
            label.color = AvStyleHost.Resolve(style.Color, AvTheme.Dim);
            if (fill != null) fill.color = AvStyleHost.Resolve(style.Background, Color.clear);
        }

        /// <summary>
        /// A record the file will not show: solid bars with a short last line, standing in
        /// for a photograph or a field that is not on this copy.
        /// </summary>
        private static GameObject Redaction(RectTransform parent, Rect area, int lines)
        {
            var block = new GameObject("Redaction", typeof(RectTransform));
            var rect = (RectTransform)block.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, area);

            Color bar = AvStyleHost.Resolve(AvStyleHost.Style("redact").Background,
                                            AvTheme.Dim.WithAlpha(0.35f));
            float gap = 4f;
            float height = Mathf.Max(2f, (area.height - gap * (lines - 1)) / lines);
            for (int i = 0; i < lines; i++)
            {
                float w = i == lines - 1 ? Mathf.Max(6f, area.width * 0.62f) : area.width;
                AvKit.Rule(rect, new Rect(0f, -i * (height + gap), w, height), bar);
            }
            return block;
        }

        /// <summary>
        /// A dog-eared corner: one diagonal cut with its crease, laid into the sheet at the
        /// angle that points into the form.
        /// </summary>
        private static void CornerFold(
            RectTransform parent, float cornerX, float cornerY, float size, float angle, Color colour)
        {
            var go = new GameObject("CornerFold", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            AvKit.Place(rect, new Rect(cornerX, cornerY, 1f, 1f));

            AvKit.Rule(rect, new Rect(0f, 0f, size, 1f), colour);
            AvKit.Rule(rect, new Rect(0f, -3f, size * 0.72f, 1f), colour.WithAlpha(colour.a * 0.45f));
        }

        /// <summary>The page spine plus punched filing holes, so the stack reads as bound.</summary>
        private static void DossierSpine(RectTransform parent, Rect body)
        {
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            for (int i = 0; i < 3; i++)
            {
                float y = body.y - body.height * (0.18f + i * 0.30f);
                Image hole = AvStyled.Box(parent, new Rect(body.x, y + 2.5f, 5f, 5f), "punch");
                if (hole != null) hole.raycastTarget = false;
            }
        }

        private static void RowSeparator(RectTransform parent, Rect area) =>
            AvKit.Rule(parent, new Rect(area.x, area.y - area.height, area.width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        private static TMP_Text KeyValue(
            RectTransform parent, float x, float y, float width, string key)
        {
            const float keyShare = 0.60f;
            TMP_Text keyLabel = PlainLabel(parent, new Rect(x, y, width * keyShare, 16f), key, "form-key");
            float keyWidth = Mathf.Min(width * keyShare,
                Mathf.Ceil(keyLabel.GetPreferredValues(key).x) + 6f);
            DottedLeader(parent, x + keyWidth, y, width * keyShare - keyWidth - 4f);

            TMP_Text value = PlainLabel(parent, new Rect(x + width * keyShare, y, width * 0.40f, 16f),
                                        "—", "form-value");
            value.alignment = TextAlignmentOptions.MidlineRight;
            return value;
        }

        private static Color RailColour(string state) =>
            AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);

        private static Color HoverFill() =>
            AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised);

        /// <summary>Small vector hunt/ace mark, drawn into an area without allocating art.</summary>
        private static void Glyph(RectTransform parent, Rect area, HuntMark mark, Color color)
        {
            var go = new GameObject(mark.ToString(), typeof(RectTransform), typeof(HuntGlyph));
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
                string key = localProfile.Body + "|" + localProfile.Face + "|" + localProfile.Hair + "|" +
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
            profilePortraitKey = null;
            return WingLink.PilotPortrait(name, callsign);
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
