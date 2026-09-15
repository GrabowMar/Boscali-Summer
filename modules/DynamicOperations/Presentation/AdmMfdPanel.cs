using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using BoscaliSummer.Features.DynamicOperations.Configuration;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Presentation
{
    /// <summary>
    /// "ADM" — solo-only admin bezel. Stage 1 is the STR tasking board on its own
    /// screen so it can grow without living inside Command.
    /// </summary>
    internal sealed class AdmMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;
        private const int ChipCount = 3;
        private const int TabTasking = 0;
        private const int TaskRowCount = 3;

        private DynamicOperationsSettings settings;
        private ManualLogSource logger;
        private ISecondaryObjectivesView tasking;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvScreen shell;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        private readonly ListRow[] taskRows = new ListRow[TaskRowCount];
        private TMP_Text taskNote;

        public void Configure(DynamicOperationsSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
            ModServices.TryGet(out tasking);
        }

        public void ResetForScene()
        {
            TeardownScreen();
            tasking = null;
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || settings == null) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (!GameAccess.IsSoloAuthority())
            {
                if (screen != null) TeardownScreen();
                return;
            }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (!screen.isActive || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void TeardownScreen()
        {
            MfdScreenHost.Release(MfdSlots.Adm);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            shell = null;
            Array.Clear(taskRows, 0, taskRows.Length);
            taskNote = null;
        }

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd =
                    SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdScreenHost.TryHost(MfdSlots.Adm, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    logger?.LogWarning("ADM MFD unavailable: could not add a host button.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdScreenHost.Release(MfdSlots.Adm);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdScreenHost.Release(MfdSlots.Adm);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    TeardownScreen();
                    failed = true;
                    logger?.LogWarning("ADM MFD unavailable: claimed bezel changed before binding.");
                    return;
                }
                logger?.LogInfo("ADM MFD installed on " + (left ? "left" : "right") +
                                " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                TeardownScreen();
                failed = true;
                logger?.LogError("ADM MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            TMP_FontAsset font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliAdmin.Screen", typeof(RectTransform), typeof(Image));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

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

            ModServices.TryGet(out tasking);

            shell = AvScreen.Build(
                content, "ADM",
                new[] { "TASKING" },
                new[]
                {
                    new[] { "BOARD", "STATUS" },
                    new[] { "CARDS", "ISSUED" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            BuildTaskingPage(shell.CreatePage(TabTasking, "TaskingPage"));

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Adm;
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                shell = null;
                return null;
            }

            screenRoot = root;
            shell.SetPage(TabTasking);
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

        private static void Divider(RectTransform parent, float x, float y, float width) =>
            AvKit.Rule(parent, new Rect(x, y, width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));

        private sealed class ListRow
        {
            public const float Pitch = 50f;

            private readonly GameObject root;
            private readonly Image rail;
            private readonly TMP_Text name;
            private readonly TMP_Text detail;
            private readonly TMP_Text value;
            private readonly Image bar;

            public ListRow(RectTransform parent, float x, float y, float width)
            {
                root = new GameObject("ListRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, Pitch - 4f));

                const float trail = 96f;
                float textWidth = width - trail - 20f;

                rail = AvStyled.Rail(rect, new Rect(0f, 0f, 3f, Pitch - 8f), "locked");
                name = AvStyled.Label(rect, new Rect(12f, 0f, textWidth, 15f), "", "row-name");
                detail = AvStyled.Label(rect, new Rect(12f, -16f, textWidth, 28f), "", "row-sub");
                value = AvStyled.Label(rect, new Rect(width - trail, 0f, trail, 15f), "",
                                       "row-value", align: TextAlignmentOptions.MidlineRight);
                bar = AvKit.ProgressBar(rect, new Rect(width - trail, -22f, trail, 6f), 0f,
                                        AvTheme.RailReady);

                Divider(rect, 0f, -(Pitch - 8f), width);
                root.SetActive(false);
            }

            public void Bind(string railState, string title, string sub, string figure,
                             float fraction, Color figureColor, Color barColor)
            {
                rail.color = AvStyleHost.Resolve(
                    AvStyleHost.Style("rail " + railState).Background, AvTheme.RailInert);

                name.text = title ?? "";
                detail.text = sub ?? "";
                value.text = figure ?? "";
                value.color = figureColor;

                bar.color = barColor;
                bar.fillAmount = Mathf.Clamp01(fraction);

                if (!root.activeSelf) root.SetActive(true);
            }

            public void Hide()
            {
                if (root.activeSelf) root.SetActive(false);
            }
        }

        private void BuildTaskingPage(GameObject page)
        {
            var parent = (RectTransform)page.transform;
            Rect body = shell.Body;
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;
            float y = body.y;

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            y = SectionHeader(parent, x, y, width, "FACTION TASKING", "SECONDARY OBJECTIVES", band: false);

            if (tasking == null)
            {
                AvStyled.Label(parent, new Rect(x, y, width, 40f),
                               "Dynamic operations are not running on this host. " +
                               "Nothing is issuing faction tasking.", "row-sub");
                return;
            }

            AvStyled.Button(parent, new Rect(x, y - 4f, 110f, 24f), "REQUEST BOARD", "btn",
                            () => { tasking.Refresh(); nextRefresh = 0f; })
                    .WithTooltip("Ask the host for the current faction objective board. " +
                                 "The board is issued by the host; this does not create work.");
            y -= 32f;

            taskNote = AvStyled.Label(parent, new Rect(x, y, width, 30f), "", "row-sub");
            y -= 36f;

            for (int i = 0; i < taskRows.Length; i++)
            {
                taskRows[i] = new ListRow(parent, x, y - i * ListRow.Pitch, width);
            }
        }

        private void RefreshTasking()
        {
            if (tasking == null || taskNote == null) return;

            taskNote.text = tasking.Status ?? "";
            taskNote.color = AvTheme.Dim;

            IReadOnlyList<SecondaryObjectiveView> cards = tasking.Objectives;
            int count = cards == null ? 0 : cards.Count;

            for (int i = 0; i < taskRows.Length; i++)
            {
                if (i >= count)
                {
                    taskRows[i].Hide();
                    continue;
                }

                SecondaryObjectiveView card = cards[i];
                bool active = card.IsActive;

                string rail = card.IsComplete ? "ready" : active ? "armed" : "locked";
                Color tint = card.IsComplete ? AvTheme.RailReady
                           : active ? AvTheme.RailCaution
                           : AvTheme.Disabled;

                string detail = card.Target + " · " + card.Status +
                                (active ? " · " + Countdown(card.SecondsRemaining) : "") +
                                "\n" + card.Reward;

                taskRows[i].Bind(
                    rail,
                    card.Title,
                    detail,
                    Percent(card.Progress),
                    card.Progress,
                    tint,
                    tint);
            }
        }

        private void Refresh()
        {
            if (shell == null) return;

            IReadOnlyList<SecondaryObjectiveView> cards = tasking != null ? tasking.Objectives : null;
            int count = cards == null ? 0 : cards.Count;
            int active = 0;
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i].IsActive) active++;
                }
            }

            if (tasking == null)
            {
                shell.DataBar.State.text = "—";
                shell.DataBar.State.color = AvTheme.Dim;
                shell.DataBar.SetChip(0, "SOLO", "live");
                shell.DataBar.SetChip(1, "NO BOARD", "inert");
                shell.DataBar.SetChip(2, "0/3", "inert");
                shell.Metrics[0].Set("—", "NO TASKING", 0f, AvTheme.RailInert);
                shell.Metrics[1].Set("—", "NO CARDS", 0f, AvTheme.RailInert);
                shell.WriteStatus(null, MapPicker.Prompt, "Dynamic operations are not running on this host.");
                return;
            }

            shell.DataBar.State.text = count > 0 ? "TASKING" : "EMPTY BOARD";
            shell.DataBar.State.color = count > 0 ? AvTheme.Dim : AvTheme.RailCaution;
            shell.DataBar.SetChip(0, "SOLO", "live");
            shell.DataBar.SetChip(1, count > 0 ? "BOARD" : "EMPTY", count > 0 ? "live" : "inert");
            shell.DataBar.SetChip(2, count + "/3", count > 0 ? "live" : "inert");

            shell.Metrics[0].Set(
                count > 0 ? "LIVE" : "IDLE",
                active + " ACTIVE",
                count > 0 ? 1f : 0f,
                count > 0 ? AvTheme.RailReady : AvTheme.RailInert);
            shell.Metrics[1].Set(
                count.ToString(),
                count + " CARD" + (count == 1 ? "" : "S"),
                count / 3f,
                count > 0 ? AvTheme.RailInfo : AvTheme.RailInert);

            RefreshTasking();
            shell.WriteStatus(null, MapPicker.Prompt, tasking.Status ?? "TASKING");
        }

        private static string Percent(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return "—";
            float clamped = ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
            return ((int)Math.Round(clamped * 100f, MidpointRounding.AwayFromZero))
                   .ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string Countdown(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "—";
            if (seconds <= 0f) return "EXPIRED";

            int total = (int)Math.Ceiling(seconds);
            if (total < 60) return "T-" + total + "s";

            int minutes = total / 60;
            int rest = total % 60;
            return "T-" + minutes + ":" + rest.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
