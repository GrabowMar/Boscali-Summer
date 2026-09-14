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
                new[] { "PILOT", "SKILLS", "WINGS", "STUDIO" },
                new[]
                {
                    new[] { "PILOT SCORE", "PTS · THIS PILOT" },
                    new[] { "SKILL POINTS", "UNSPENT" },
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
                ? "DEBUG BYPASS — ALL SKILLS ACTIVE" : "PILOT DOSSIER & SHARED SKILL BOARD";
            dataBar.State.color = hunted ? AvTheme.Alert : bypass ? AvTheme.Warning : AvTheme.RailReady;
            dataBar.SetChip(0, hunted ? "HUNT ACTIVE" : "NO HUNT", hunted);
            dataBar.SetChip(1, "RANK " + Progress.Rank, true);
            int available = Progress.AvailablePoints;
            dataBar.SetChip(2, bypass ? "ALL ACTIVE" : available + "P AVAIL", available > 0 || bypass);
        }

        private void RefreshMetrics(bool bypass, int score, int bonus)
        {
            IProgressionView view = Progress;
            int perPoint = Math.Max(1, view.ScorePerPoint);
            int intoPoint = score % perPoint;
            int ceiling = Mathf.Max(1, view.MaximumPoints);
            int available = view.AvailablePoints;
            bool capped = view.EarnedPoints >= ceiling;

            scoreMetric.Set(
                bypass ? "BYPASS" : score.ToString("N0"),
                bypass ? "ALL SKILLS ACTIVE" : capped ? "SCORE BUDGET COMPLETE" : (perPoint - intoPoint) + " PTS TO NEXT PERK",
                bypass || capped ? 1f : intoPoint / (float)perPoint,
                bypass ? AvTheme.Warning : AvTheme.RailReady);
            scoreMetric.Unit.text = "PTS · THIS PILOT";

            int earned = view.EarnedPoints;
            budgetMetric.Set(
                bypass ? "FREE" : available + "P",
                bypass ? "UNLIMITED POINTS" : $"{earned}/{ceiling} EARNED · {bonus} ACE BONUS",
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
                : progression.BypassRequirements ? "DEBUG BYPASS — ALL SKILLS ACTIVE"
                : progression.LastResult;
            shell.WriteStatus(null, MapPicker.Prompt, baseLine);
        }

        // ---- Shared drawing helpers ------------------------------------------------------

        private static float DrawSectionTitle(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
        {
            if (band) AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(parent, x - SpineInset + 3f, y - 7f);

            float half = width * 0.50f;
            AvStyled.Label(parent, new Rect(x, y, half, 14f), title, "section-title");
            if (!string.IsNullOrEmpty(note))
            {
                AvStyled.Label(parent, new Rect(x + half, y, width - half, 14f), note,
                               "section-title-note", align: TextAlignmentOptions.MidlineRight);
            }
            return y - 22f;
        }

        private static void RowSeparator(RectTransform parent, Rect area) =>
            AvKit.Rule(parent, new Rect(area.x, area.y - area.height, area.width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        private static TMP_Text KeyValue(
            RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.60f, 16f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.60f, y, width * 0.40f, 16f),
                                  "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        private static Color RailColour(string state) =>
            AvStyleHost.Resolve(AvStyleHost.Style("rail " + state).Background, AvTheme.RailInert);

        private static Color HoverFill() =>
            AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised);

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

        private static void SetPortrait(Image image, TMP_Text fallback, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
            fallback.gameObject.SetActive(sprite == null);
        }

        // ---- Row pooling -----------------------------------------------------------------

        private sealed class SkillRow
        {
            public byte Id;
            public AvButton Select;
            public AvButton Confirm;
            public Image Rail;
            public Image Background;
            public SqdGlyph Icon;
            public TMP_Text Code;
            public TMP_Text Name;
        }

        private sealed class WingRow
        {
            public RectTransform Root;
            public GameObject Empty;
            public Image Rail;
            public Image Crest;
            public string CrestKey;
            public Image Portrait;
            public TMP_Text PortraitFallback;
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
