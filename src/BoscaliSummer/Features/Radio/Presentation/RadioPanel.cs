using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Runtime;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>
    /// A compact music surface on the maximised-map MFD. It follows the same safe seam as
    /// Nuclear Option's native screens: claim an unused bezel slot, build known widgets,
    /// and borrow only the active font and theme colours.
    /// </summary>
    internal static class RadioPanel
    {
        private const float Width = 410f;
        private const float Height = 556f;
        private const float Pad = 16f;
        private const int RowsPerPage = 5;

        private sealed class ChannelRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image BadgeGround;
            public Image Icon;
            public TMP_Text Badge;
            public TMP_Text Label;
            public TMP_Text Count;
            public Button Button;
        }

        private static readonly ChannelRow[] rows = new ChannelRow[RowsPerPage];
        private static MFDScreen screen;
        private static TMP_FontAsset font;
        private static RadioManager manager;
        private static TMP_Text channelLabel;
        private static Image stationIconGround;
        private static Image stationIcon;
        private static TMP_Text stationBadge;
        private static TMP_Text trackLabel;
        private static TMP_Text timeLabel;
        private static TMP_Text pageLabel;
        private static TMP_Text volumeLabel;
        private static TMP_Text statusLabel;
        private static Image progressFill;
        private static Button playButton;
        private static TMP_Text playButtonLabel;
        private static Button shuffleButton;
        private static TMP_Text shuffleButtonLabel;
        private static Button repeatButton;
        private static TMP_Text repeatButtonLabel;
        private static int page;
        private static int iconRevision = -1;
        private static float nextAttempt;
        private static float nextRefresh;
        private static bool unavailableLogged;
        private static bool gaveUp;

        public static void Tick(RadioManager radio)
        {
            manager = radio;
            if (gaveUp) return;
            if (!GameAccess.MfdAvailable)
            {
                if (!unavailableLogged)
                {
                    unavailableLogged = true;
                    Plugin.Logger.LogWarning("Radio panel unavailable: VirtualMFD access did not resolve.");
                }
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
            nextRefresh = Time.unscaledTime + 0.15f;
            Refresh();
        }

        public static void Reset()
        {
            RadioStationIconCache.Clear();
            screen = null;
            font = null;
            manager = null;
            channelLabel = null;
            stationIconGround = null;
            stationIcon = null;
            stationBadge = null;
            trackLabel = null;
            timeLabel = null;
            pageLabel = null;
            volumeLabel = null;
            statusLabel = null;
            progressFill = null;
            playButton = null;
            playButtonLabel = null;
            shuffleButton = null;
            shuffleButtonLabel = null;
            repeatButton = null;
            repeatButtonLabel = null;
            for (int i = 0; i < rows.Length; i++) rows[i] = null;
            page = 0;
            iconRevision = -1;
            nextAttempt = 0f;
            nextRefresh = 0f;
            gaveUp = false;
        }

        private static void TryInstall()
        {
            try
            {
                VirtualMFD mfd = UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                // Prefer the right bezel so a co-installed tactical command panel can keep
                // the conventional left-side management position.
                List<Button> buttons = GameAccess.GetRightMfdButtons(mfd);
                List<MFDScreen> screens = GameAccess.GetRightMfdScreens(mfd);
                bool right = true;
                if (!TryClaimSlot(buttons, screens, out int slot))
                {
                    buttons = GameAccess.GetLeftMfdButtons(mfd);
                    screens = GameAccess.GetLeftMfdScreens(mfd);
                    right = false;
                    if (!TryClaimSlot(buttons, screens, out slot))
                    {
                        Fail("no free bezel slot");
                        return;
                    }
                }

                MFDScreen template = FindTemplate(screens) ??
                    FindTemplate(GameAccess.GetRightMfdScreens(mfd)) ??
                    FindTemplate(GameAccess.GetLeftMfdScreens(mfd));
                if (template == null) return;

                screen = Build(template, buttons[slot]);
                if (screen == null) return;
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
                Plugin.Logger.LogInfo("Radio MFD installed on " + (right ? "right" : "left") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                Fail(e.Message);
                Plugin.Logger.LogError("Radio MFD install failed: " + e);
            }
        }

        private static bool TryClaimSlot(List<Button> buttons, List<MFDScreen> screens, out int slot)
        {
            slot = -1;
            if (buttons == null || screens == null) return false;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] == null) continue;
                if (i >= screens.Count || screens[i] == null)
                {
                    slot = i;
                    return true;
                }
            }
            return false;
        }

        private static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            if (screens == null) return null;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i] != null && screens[i].transform.parent != null)
                    return screens[i];
            return null;
        }

        private static MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text anyText = template.GetComponentInChildren<TMP_Text>(true);
            font = anyText != null ? anyText.font : null;

            var root = new GameObject("BoscaliRadio.Screen", typeof(RectTransform), typeof(Image));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);
            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;
            rootRect.anchoredPosition = templateRect.anchoredPosition;
            rootRect.sizeDelta = new Vector2(Width, Height);

            Image background = root.GetComponent<Image>();
            background.color = new Color(0.025f, 0.043f, 0.060f, 0.965f);
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            Stretch(content);

            Color accent = Accent();
            Label(content, "BOSCALI RADIO", new Rect(Pad, -12f, Width - Pad * 2f, 26f),
                accent, 17f, FontStyles.Normal, TextAlignmentOptions.Center);
            Rule(content, new Rect(Pad, -42f, Width - Pad * 2f, 2f), accent.WithAlpha(0.72f));

            stationIconGround = Panel(content, new Rect(Pad, -52f, 52f, 62f),
                new Color(0f, 0f, 0f, 0.52f));
            stationIcon = Panel(stationIconGround.rectTransform, new Rect(4f, -5f, 44f, 44f), Color.white);
            stationIcon.preserveAspect = true;
            stationIcon.enabled = false;
            stationBadge = Label(stationIconGround.rectTransform, "--", new Rect(0f, -4f, 52f, 44f),
                Color.white, 13f, FontStyles.Bold, TextAlignmentOptions.Center);

            channelLabel = Label(content, "LOCAL SIGNAL // CLIENT", new Rect(Pad + 62f, -52f, Width - Pad * 2f - 62f, 18f),
                Friendly(), 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            trackLabel = Label(content, "NO LOCAL TRACKS", new Rect(Pad + 62f, -74f, Width - Pad * 2f - 62f, 42f),
                Color.white, 15f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            Image progressGround = Panel(content, new Rect(Pad, -122f, Width - Pad * 2f, 6f),
                new Color(0f, 0f, 0f, 0.72f));
            var fillObject = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.SetParent(progressGround.rectTransform, false);
            Stretch(fillRect);
            progressFill = fillObject.GetComponent<Image>();
            progressFill.color = accent;
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = 0;
            progressFill.fillAmount = 0f;
            timeLabel = Label(content, "00:00 / 00:00", new Rect(Pad, -132f, Width - Pad * 2f, 18f),
                Dim(), 9f, FontStyles.Normal, TextAlignmentOptions.Right);

            float controlsY = -158f;
            MakeButton(content, "PREV", new Rect(Pad, controlsY, 76f, 34f), () => manager?.Previous());
            playButton = MakeButton(content, "PLAY", new Rect(Pad + 84f, controlsY, 106f, 34f),
                () => manager?.TogglePlayback(), out playButtonLabel);
            MakeButton(content, "NEXT", new Rect(Pad + 198f, controlsY, 76f, 34f), () => manager?.Next());
            MakeButton(content, "STOP", new Rect(Pad + 282f, controlsY, 96f, 34f), () => manager?.Stop());

            float modesY = -200f;
            shuffleButton = MakeButton(content, "SHUFFLE", new Rect(Pad, modesY, 88f, 28f),
                () => manager?.ToggleShuffle(), out shuffleButtonLabel);
            repeatButton = MakeButton(content, "REPEAT", new Rect(Pad + 96f, modesY, 88f, 28f),
                () => manager?.ToggleRepeat(), out repeatButtonLabel);
            MakeButton(content, "FOLDER", new Rect(Pad + 192f, modesY, 88f, 28f),
                () => manager?.OpenLibraryFolder());
            MakeButton(content, "RESCAN", new Rect(Pad + 288f, modesY, 90f, 28f), () => manager?.Rescan());

            Label(content, "CHANNELS", new Rect(Pad, -240f, Width - Pad * 2f, 20f),
                accent, 11f, FontStyles.Bold, TextAlignmentOptions.Left);
            Rule(content, new Rect(Pad, -262f, Width - Pad * 2f, 1f), accent.WithAlpha(0.42f));

            for (int i = 0; i < RowsPerPage; i++)
                rows[i] = MakeChannelRow(content, i, -270f - i * 34f);

            MakeButton(content, "<", new Rect(Pad, -446f, 42f, 28f), PreviousPage);
            pageLabel = Label(content, "1 / 1", new Rect(Pad + 50f, -446f, Width - Pad * 2f - 100f, 28f),
                Dim(), 10f, FontStyles.Normal, TextAlignmentOptions.Center);
            MakeButton(content, ">", new Rect(Width - Pad - 42f, -446f, 42f, 28f), NextPage);

            MakeButton(content, "-", new Rect(Pad, -486f, 42f, 28f), () => manager?.ChangeVolume(-0.05f));
            volumeLabel = Label(content, "MUSIC BUS // 65%", new Rect(Pad + 50f, -486f, Width - Pad * 2f - 100f, 28f),
                Friendly(), 10f, FontStyles.Normal, TextAlignmentOptions.Center);
            MakeButton(content, "+", new Rect(Width - Pad - 42f, -486f, 42f, 28f), () => manager?.ChangeVolume(0.05f));

            statusLabel = Label(content, "Stand by", new Rect(Pad, -524f, Width - Pad * 2f, 22f),
                Dim(), 9f, FontStyles.Normal, TextAlignmentOptions.Center);

            var result = root.AddComponent<MFDScreen>();
            result.shortName = "RAD";
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel == null ? null : bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                Fail("bezel label or highlight missing");
                return null;
            }

            Refresh();
            return result;
        }

        private static ChannelRow MakeChannelRow(RectTransform parent, int row, float y)
        {
            var root = new GameObject("Channel" + row, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, new Rect(Pad, y, Width - Pad * 2f, 30f));
            Image ground = root.GetComponent<Image>();
            ground.color = new Color(0f, 0f, 0f, 0.42f);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = ground;
            button.colors = ButtonColors(false);

            var result = new ChannelRow
            {
                Root = root,
                Ground = ground,
                Button = button
            };
            result.BadgeGround = Panel(rect, new Rect(5f, 4f, 28f, 22f), new Color(0.15f, 0.35f, 0.20f, 1f));
            result.Icon = Panel(result.BadgeGround.rectTransform, new Rect(2f, -1f, 24f, 24f), Color.white);
            result.Icon.preserveAspect = true;
            result.Icon.enabled = false;
            result.Badge = Label(result.BadgeGround.rectTransform, "--", new Rect(0f, 0f, 28f, 22f),
                Color.white, 9f, FontStyles.Bold, TextAlignmentOptions.Center);
            result.Label = Label(rect, "LOCAL", new Rect(41f, 0f, Width - Pad * 2f - 101f, 30f),
                Color.white, 10f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            result.Count = Label(rect, "0", new Rect(Width - Pad * 2f - 58f, 0f, 48f, 30f),
                Dim(), 9f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            button.onClick.AddListener(() =>
            {
                if (manager != null && result.Index >= 0) manager.SelectChannel(result.Index);
                nextRefresh = 0f;
            });
            return result;
        }

        private static void Refresh()
        {
            if (manager == null || channelLabel == null) return;
            if (iconRevision != manager.StationRevision)
            {
                RadioStationIconCache.Clear();
                iconRevision = manager.StationRevision;
            }
            int pages = Math.Max(1, (manager.ChannelCount + RowsPerPage - 1) / RowsPerPage);
            page = Mathf.Clamp(page, 0, pages - 1);

            channelLabel.text = manager.CurrentChannelCode + " // " + manager.CurrentChannelName + " // LOCAL SIGNAL";
            Color currentColor = manager.GetChannelColor(manager.SelectedChannel);
            stationIconGround.color = new Color(
                currentColor.r * 0.22f, currentColor.g * 0.22f, currentColor.b * 0.22f, 0.96f);
            ApplyStationIcon(
                stationIcon, stationBadge, manager.GetChannelIconPath(manager.SelectedChannel),
                manager.CurrentChannelCode);
            trackLabel.text = manager.CurrentTrackTitle;
            progressFill.fillAmount = manager.Progress;
            timeLabel.text = FormatTime(manager.Elapsed) + " / " + FormatTime(manager.Duration);
            playButtonLabel.text = manager.IsPaused ? "RESUME" : manager.IsEngaged ? "PAUSE" : "PLAY";
            shuffleButtonLabel.text = manager.Shuffle ? "SHUFFLE ON" : "SHUFFLE";
            repeatButtonLabel.text = manager.RepeatTrack ? "REPEAT ON" : "REPEAT";
            SetLatched(shuffleButton, manager.Shuffle);
            SetLatched(repeatButton, manager.RepeatTrack);
            volumeLabel.text = "MUSIC BUS // " + Mathf.RoundToInt(manager.Volume * 100f) + "%";
            statusLabel.text = manager.Status;
            pageLabel.text = (page + 1) + " / " + pages;

            for (int row = 0; row < rows.Length; row++)
            {
                int index = page * RowsPerPage + row;
                ChannelRow item = rows[row];
                item.Index = index < manager.ChannelCount ? index : -1;
                item.Root.SetActive(item.Index >= 0);
                if (item.Index < 0) continue;
                item.Badge.text = manager.GetChannelCode(index);
                ApplyStationIcon(
                    item.Icon, item.Badge, manager.GetChannelIconPath(index),
                    manager.GetChannelCode(index));
                Color stationColor = manager.GetChannelColor(index);
                item.BadgeGround.color = new Color(
                    stationColor.r * 0.34f, stationColor.g * 0.34f, stationColor.b * 0.34f, 1f);
                item.Label.text = manager.GetChannelName(index);
                item.Count.text = manager.GetChannelTrackCount(index) + " TRK";
                item.Button.colors = ButtonColors(index == manager.SelectedChannel);
            }
        }

        private static void ApplyStationIcon(
            Image image, TMP_Text fallback, string path, string fallbackText)
        {
            Sprite sprite = RadioStationIconCache.Get(path);
            bool available = sprite != null;
            image.sprite = sprite;
            image.enabled = available;
            fallback.text = fallbackText;
            fallback.gameObject.SetActive(!available);
        }

        private static void PreviousPage()
        {
            if (page > 0) page--;
            nextRefresh = 0f;
        }

        private static void NextPage()
        {
            int pages = manager == null ? 1 : Math.Max(1,
                (manager.ChannelCount + RowsPerPage - 1) / RowsPerPage);
            if (page + 1 < pages) page++;
            nextRefresh = 0f;
        }

        private static Button MakeButton(RectTransform parent, string text, Rect rect, Action action) =>
            MakeButton(parent, text, rect, action, out _);

        private static Button MakeButton(
            RectTransform parent, string text, Rect rect, Action action, out TMP_Text label)
        {
            var root = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, rect);
            Image image = root.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.52f);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = ButtonColors(false);
            button.onClick.AddListener(() =>
            {
                action?.Invoke();
                nextRefresh = 0f;
            });
            label = Label(rt, text, new Rect(0f, 0f, rect.width, rect.height),
                Color.white, 10f, FontStyles.Normal, TextAlignmentOptions.Center);
            return button;
        }

        private static ColorBlock ButtonColors(bool selected)
        {
            Color accent = Accent();
            return new ColorBlock
            {
                normalColor = selected ? new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 0.96f)
                    : new Color(0.025f, 0.055f, 0.065f, 0.94f),
                highlightedColor = new Color(accent.r * 0.38f, accent.g * 0.38f, accent.b * 0.38f, 1f),
                pressedColor = new Color(accent.r * 0.56f, accent.g * 0.56f, accent.b * 0.56f, 1f),
                selectedColor = new Color(accent.r * 0.34f, accent.g * 0.34f, accent.b * 0.34f, 1f),
                disabledColor = new Color(0.18f, 0.20f, 0.21f, 0.68f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
        }

        private static void SetLatched(Button button, bool selected)
        {
            if (button != null) button.colors = ButtonColors(selected);
        }

        private static TMP_Text Label(
            RectTransform parent, string text, Rect rect, Color color, float size,
            FontStyles style, TextAlignmentOptions alignment)
        {
            var root = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, rect);
            TextMeshProUGUI label = root.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = color;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            if (font != null) label.font = font;
            return label;
        }

        private static Image Panel(RectTransform parent, Rect rect, Color color)
        {
            var root = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, rect);
            Image image = root.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image Rule(RectTransform parent, Rect rect, Color color) => Panel(parent, rect, color);

        private static void Place(RectTransform target, Rect rect)
        {
            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = new Vector2(rect.x, rect.y);
            target.sizeDelta = new Vector2(rect.width, rect.height);
            target.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform target)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            target.localScale = Vector3.one;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject)
                    return images[i];
            return button.GetComponent<Image>();
        }

        private static string FormatTime(float seconds)
        {
            int value = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (value / 60).ToString("00") + ":" + (value % 60).ToString("00");
        }

        private static Color Accent()
        {
            try { return ThemeManager.Active.ColorTheme.AllClear; }
            catch { return new Color(0.30f, 1f, 0.35f); }
        }

        private static Color Friendly()
        {
            try { return ThemeManager.Active.ColorTheme.MapIconFriendly; }
            catch { return new Color(0.45f, 0.95f, 0.55f); }
        }

        private static Color Dim() => new Color(0.66f, 0.71f, 0.73f, 1f);

        private static Color WithAlpha(this Color color, float alpha) =>
            new Color(color.r, color.g, color.b, alpha);

        private static void Fail(string reason)
        {
            gaveUp = true;
            screen = null;
            Plugin.Logger.LogWarning("Radio panel disabled (" + reason + "). Playback remains available through config reload only.");
        }
    }
}
