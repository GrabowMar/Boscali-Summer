using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    internal sealed class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = 430f;
        private const float Height = 590f;
        private readonly List<Button> skillButtons = new List<Button>();
        private readonly List<TMP_Text> skillLabels = new List<TMP_Text>();
        private readonly Dictionary<SupportActionId, Button> supportButtons =
            new Dictionary<SupportActionId, Button>();
        private readonly Dictionary<SupportActionId, TMP_Text> supportLabels =
            new Dictionary<SupportActionId, TMP_Text>();
        private readonly Dictionary<SupportActionId, string> supportTitles =
            new Dictionary<SupportActionId, string>();
        private SupportManager support;
        private IProgressionView progression;
        private ManualLogSource logger;
        private MFDScreen screen;
        private TMP_FontAsset font;
        private GameObject skillsPage;
        private GameObject supportPage;
        private TMP_Text summary;
        private TMP_Text status;
        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        public void Configure(SupportManager manager, IProgressionView progressionView, ManualLogSource log)
        {
            support = manager;
            progression = progressionView;
            logger = log;
        }

        public void ResetForScene()
        {
            screen = null;
            font = null;
            skillsPage = null;
            supportPage = null;
            summary = null;
            status = null;
            skillButtons.Clear();
            skillLabels.Clear();
            supportButtons.Clear();
            supportLabels.Clear();
            supportTitles.Clear();
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void Update()
        {
            if (failed || support == null || progression == null) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }
            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }
            if (screen.isActive && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                Refresh();
            }
        }

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;
                List<Button> buttons = GameAccess.GetLeftMfdButtons(mfd);
                List<MFDScreen> screens = GameAccess.GetLeftMfdScreens(mfd);
                bool right = false;
                if (!TryClaim(buttons, screens, out int slot))
                {
                    buttons = GameAccess.GetRightMfdButtons(mfd);
                    screens = GameAccess.GetRightMfdScreens(mfd);
                    right = true;
                    if (!TryClaim(buttons, screens, out slot))
                    {
                        failed = true;
                        logger.LogWarning("OPS MFD unavailable: no free bezel slot.");
                        return;
                    }
                }
                MFDScreen template = FindTemplate(screens) ??
                    FindTemplate(GameAccess.GetLeftMfdScreens(mfd)) ??
                    FindTemplate(GameAccess.GetRightMfdScreens(mfd));
                if (template == null) return;
                screen = Build(template, buttons[slot]);
                if (screen == null) { failed = true; return; }
                while (screens.Count <= slot) screens.Add(null);
                screens[slot] = screen;
                mfd.SetupButtons();
                Button bezel = buttons[slot];
                bezel.enabled = true;
                bezel.interactable = true;
                if (bezel.onClick.GetPersistentEventCount() == 0)
                {
                    VirtualMFD owner = mfd;
                    bool onRight = right;
                    bezel.onClick.AddListener(() =>
                    {
                        if (onRight) owner.PressRightButton(bezel);
                        else owner.PressLeftButton(bezel);
                    });
                }
                screen.CloseScreen(Screen.width * (right ? Vector3.right : Vector3.left));
                logger.LogInfo($"OPS MFD installed on {(right ? "right" : "left")} bezel slot {slot + 1}.");
            }
            catch (Exception e)
            {
                failed = true;
                logger.LogError("OPS MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            font = sourceText != null ? sourceText.font : null;
            var root = new GameObject("BoscaliOperations.Screen", typeof(RectTransform), typeof(Image));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(template.transform.parent, false);
            RectTransform templateRect = (RectTransform)template.transform;
            rect.anchorMin = templateRect.anchorMin; rect.anchorMax = templateRect.anchorMax;
            rect.pivot = templateRect.pivot; rect.anchoredPosition = templateRect.anchoredPosition;
            rect.localScale = templateRect.localScale; rect.sizeDelta = new Vector2(Width, Height);
            root.GetComponent<Image>().color = new Color(0.028f, 0.045f, 0.062f, 0.97f);

            Label(rect, "OPERATIONS", new Rect(12f, -10f, 406f, 30f), Accent(), 18f, TextAlignmentOptions.Center);
            Button skillsTab = MakeButton(rect, "SKILLS", new Rect(12f, -48f, 199f, 32f), ShowSkills);
            Button supportTab = MakeButton(rect, "SUPPORT", new Rect(219f, -48f, 199f, 32f), ShowSupport);

            skillsPage = Page(rect, "SkillsPage");
            summary = Label((RectTransform)skillsPage.transform, "", new Rect(16f, -92f, 398f, 28f), Color.white, 12f, TextAlignmentOptions.Left);
            ProgressionSkillView[] skills = progression.GetSkills();
            for (int i = 0; i < skills.Length; i++)
            {
                int index = i;
                float y = -126f - i * 43f;
                Button button = MakeButton((RectTransform)skillsPage.transform, skills[i].Name,
                    new Rect(16f, y, 398f, 38f), () => progression.RequestUnlock(skills[index].Id));
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(10f, 0f, 8f, 0f);
                skillButtons.Add(button);
                skillLabels.Add(label);
            }

            supportPage = Page(rect, "SupportPage");
            Label((RectTransform)supportPage.transform,
                "Place the cursor on the maximized map, then request support.",
                new Rect(16f, -96f, 398f, 45f), Color.white, 11f, TextAlignmentOptions.Center);
            MakeSupportButton(SupportActionId.VehicleAirdrop, "VEHICLE AIRDROP", -156f);
            MakeSupportButton(SupportActionId.Artillery, "ARTILLERY FIRE MISSION", -226f);
            MakeSupportButton(SupportActionId.FortifyZone, "FORTIFY CONTROLLED ZONE", -296f);
            status = Label((RectTransform)supportPage.transform, "", new Rect(16f, -380f, 398f, 90f),
                new Color(0.75f, 0.82f, 0.84f), 11f, TextAlignmentOptions.Center);
            status.enableWordWrapping = true;

            var result = root.AddComponent<MFDScreen>();
            result.shortName = "OPS";
            result.displayPanel = root;
            result.aircraftOnly = false;
            result.label = bezel?.GetComponentInChildren<TextMeshProUGUI>(true);
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }
            ShowSkills();
            Refresh();
            return result;
        }

        private void MakeSupportButton(SupportActionId action, string title, float y)
        {
            Button button = MakeButton((RectTransform)supportPage.transform,
                $"{title}   [{support.Cost(action):0.#}]", new Rect(16f, y, 398f, 54f),
                () => support.RequestAtMapCursor(action));
            button.GetComponentInChildren<TMP_Text>(true).fontSize = 12f;
            supportButtons[action] = button;
            supportLabels[action] = button.GetComponentInChildren<TMP_Text>(true);
            supportTitles[action] = title;
        }

        private void Refresh()
        {
            if (summary == null) return;
            summary.text = $"VANILLA RANK {progression.Rank}     AVAILABLE POINTS {progression.AvailablePoints}";
            ProgressionSkillView[] skills = progression.GetSkills();
            int count = Mathf.Min(skills.Length, skillButtons.Count);
            for (int i = 0; i < count; i++)
            {
                ProgressionSkillView skill = skills[i];
                skillButtons[i].interactable = skill.Available;
                skillLabels[i].text = (skill.Unlocked ? "[UNLOCKED] " : "") + skill.Name + " — " + skill.Description;
                skillLabels[i].color = skill.Unlocked ? Accent() : skill.Available ? Color.white : new Color(0.55f, 0.58f, 0.60f);
            }
            foreach (KeyValuePair<SupportActionId, Button> entry in supportButtons)
            {
                bool available = support.CanRequestLocally(entry.Key);
                entry.Value.interactable = available;
                supportLabels[entry.Key].text = $"{supportTitles[entry.Key]}   [{support.Cost(entry.Key):0.#}]" +
                    (available ? string.Empty : "   [LOCKED/OFF]");
            }
            if (status != null) status.text = support.Status + "\n" + progression.Status;
        }

        private void ShowSkills()
        {
            skillsPage?.SetActive(true);
            supportPage?.SetActive(false);
            nextRefresh = 0f;
        }

        private void ShowSupport()
        {
            skillsPage?.SetActive(false);
            supportPage?.SetActive(true);
            nextRefresh = 0f;
        }

        private static bool TryClaim(List<Button> buttons, List<MFDScreen> screens, out int slot)
        {
            slot = -1;
            if (buttons == null || screens == null) return false;
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i] != null && (i >= screens.Count || screens[i] == null)) { slot = i; return true; }
            return false;
        }

        private static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            if (screens == null) return null;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i] != null && screens[i].transform.parent != null) return screens[i];
            return null;
        }

        private static GameObject Page(RectTransform parent, string name)
        {
            var page = new GameObject(name, typeof(RectTransform));
            RectTransform rect = page.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);
            return page;
        }

        private Button MakeButton(RectTransform parent, string text, Rect area, Action action)
        {
            var root = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false); Place(rect, area);
            Image image = root.GetComponent<Image>();
            image.color = new Color(0.05f, 0.12f, 0.13f, 0.92f);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = new Color(0.05f, 0.12f, 0.13f, 0.92f),
                highlightedColor = new Color(0.10f, 0.30f, 0.24f, 0.96f),
                pressedColor = new Color(0.18f, 0.48f, 0.34f, 1f),
                selectedColor = new Color(0.10f, 0.30f, 0.24f, 0.96f),
                disabledColor = new Color(0.03f, 0.05f, 0.06f, 0.75f),
                colorMultiplier = 1f, fadeDuration = 0.08f
            };
            button.onClick.AddListener(() => action?.Invoke());
            Label(rect, text, new Rect(0f, 0f, area.width, area.height), Accent(), 11f, TextAlignmentOptions.Center);
            return button;
        }

        private TMP_Text Label(RectTransform parent, string text, Rect area, Color color, float size, TextAlignmentOptions alignment)
        {
            var root = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false); Place(rect, area);
            TextMeshProUGUI label = root.GetComponent<TextMeshProUGUI>();
            label.text = text; label.color = color; label.fontSize = size; label.alignment = alignment;
            label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false; if (font != null) label.font = font;
            return label;
        }

        private static void Place(RectTransform target, Rect area)
        {
            target.anchorMin = new Vector2(0f, 1f); target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f); target.anchoredPosition = new Vector2(area.x, area.y);
            target.sizeDelta = new Vector2(area.width, area.height); target.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform target)
        {
            target.anchorMin = Vector2.zero; target.anchorMax = Vector2.one;
            target.offsetMin = Vector2.zero; target.offsetMax = Vector2.zero; target.localScale = Vector3.one;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++) if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        private static Color Accent()
        {
            try { return ThemeManager.Active.ColorTheme.AllClear; }
            catch { return new Color(0.30f, 1f, 0.35f); }
        }
    }
}
