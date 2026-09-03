using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Runtime;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private const float Width = 430f;

        // A four-pixel spacing rhythm. Every gap and inset on the panel is one of these,
        // and the panel's height is the sum of its sections rather than a hand-fitted
        // literal that drifts the moment a row moves.
        private const float Space1 = 4f;
        private const float Space2 = 8f;
        private const float Space3 = 12f;
        private const float Space4 = 16f;
        private const float Space5 = 20f;
        private const float Space6 = 24f;

        private const float Pad = Space3;
        private const float Gap = Space1;
        private const float RowHeight = 30f;
        private const float ControlHeight = 34f;
        private const float CardHeight = 94f;
        private const float StatusHeight = 78f;
        private const float ArtSize = 60f;
        private const float ChannelPitch = RowHeight + 2f;

        private const float FontMicro = 10f;
        private const float FontSmall = 11f;
        private const float FontLead = 14f;
        private const float FontTitle = 18f;
        private const int RowsPerPage = 5;

        private sealed class ChannelRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image SelectionRule;
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
        private static Button pagePreviousButton;
        private static Button pageNextButton;
        private static TMP_Text statusLabel;
        private static TMP_Text channelsEmptyLabel;
        private static Image progressFill;
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
            BezelRegistry.Release(BezelRegistry.Rad);
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
            pagePreviousButton = null;
            pageNextButton = null;
            statusLabel = null;
            channelsEmptyLabel = null;
            progressFill = null;
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

                if (!MfdBezel.TryClaim(BezelRegistry.Rad, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    Fail("no free bezel slot");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    BezelRegistry.Release(BezelRegistry.Rad);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    BezelRegistry.Release(BezelRegistry.Rad);
                    return;
                }

                MfdBezel.Bind(mfd, buttons, screens, slot, left, screen);
                Plugin.Logger.LogInfo("Radio MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                Fail(e.Message);
                Plugin.Logger.LogError("Radio MFD install failed: " + e);
            }
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
            // sizeDelta is set once the running cursor has measured the content, below.

            Image background = root.GetComponent<Image>();
            background.color = Unity(RadioUiPalette.PanelGround);
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            Stretch(content);

            Color accent = Accent();
            float inner = Width - Pad * 2f;

            // A running cursor down the page, on the spacing rhythm, so the panel's height
            // is the total of what it draws. The layout used two dozen hand-tuned Y
            // literals and a Height constant that had to be kept in step with them by hand.
            float y = -Pad;

            Label(content, "RADIO COMMS // AUDIO RECEIVER", new Rect(Pad, y, inner, Space5),
                accent, FontTitle, FontStyles.Bold, TextAlignmentOptions.Center);
            y -= Space5;

            // Deep-space mission tracking telemetry header strip
            float chipW = 86f;
            float chipH = 16f;
            float chipGap = 8f;
            float totalChipsW = chipW * 3f + chipGap * 2f;
            float startX = Pad + (inner - totalChipsW) * 0.5f;

            StatusChip(content, "SYS: AUDIO-NET", new Rect(startX, y, chipW, chipH),
                       Unity(RadioUiPalette.RailEmerald), Unity(RadioUiPalette.TextPrimary), FontMicro - 1f);
            StatusChip(content, "SIGNAL: HIGH", new Rect(startX + chipW + chipGap, y, chipW, chipH),
                       Unity(RadioUiPalette.RailCyan), Unity(RadioUiPalette.TextPrimary), FontMicro - 1f);
            StatusChip(content, "FREQ: VHF-COM", new Rect(startX + (chipW + chipGap) * 2f, y, chipW, chipH),
                       Unity(RadioUiPalette.RailEmerald), Unity(RadioUiPalette.TextPrimary), FontMicro - 1f);

            y -= chipH + Space2;
            Rule(content, new Rect(Pad, y, inner, 1f), Unity(RadioUiPalette.BorderSubtle));
            y -= Space2;

            // Now-playing card: station art on the left, the signal line, the track title,
            // and a progress bar. Its internals stay positioned against the card top.
            float cardTop = y;
            TacticalCard(content, new Rect(Pad, cardTop, inner, CardHeight), accent);
            stationIconGround = FramedPanel(content,
                new Rect(Pad + Space2, cardTop - Space2, ArtSize, ArtSize), Frame());
            stationIcon = Panel(stationIconGround.rectTransform,
                new Rect(Space1 + 1f, -(Space1 + 1f), ArtSize - Space2 - 2f, ArtSize - Space2 - 2f),
                Color.white);
            stationIcon.preserveAspect = true;
            stationIcon.enabled = false;
            stationBadge = Label(stationIconGround.rectTransform, "--",
                new Rect(0f, 0f, ArtSize, ArtSize),
                Color.white, FontLead, FontStyles.Bold, TextAlignmentOptions.Center);

            float infoX = Pad + Space2 + ArtSize + Space3;
            float infoW = Width - Pad - infoX;
            channelLabel = Label(content, "",
                new Rect(infoX, cardTop - Space2, infoW, Space4),
                Friendly(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            trackLabel = Label(content, "NO LOCAL TRACKS",
                new Rect(infoX, cardTop - Space5 - Space2, infoW, Space6 + Space2),
                Color.white, FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            Image progressGround = Panel(content,
                new Rect(infoX, cardTop - CardHeight + Space6, infoW, 4f),
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
            timeLabel = Label(content, "00:00 / 00:00",
                new Rect(infoX, cardTop - CardHeight + Space4, infoW, Space4),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);

            y = cardTop - CardHeight - Space3;

            // Transport: PREV and NEXT are the same size on either side of a wider PLAY;
            // STOP is no longer drawn as loud as PLAY.
            const float skip = 90f;
            const float play = 124f;
            float stop = inner - skip * 2f - play - Gap * 3f;
            MakeButton(content, "PREV", new Rect(Pad, y, skip, ControlHeight),
                RadioButtonStyle.Default, () => manager?.Previous());
            MakeButton(content, "PLAY", new Rect(Pad + skip + Gap, y, play, ControlHeight),
                RadioButtonStyle.Primary, () => manager?.TogglePlayback(), out playButtonLabel);
            MakeButton(content, "NEXT",
                new Rect(Pad + skip + play + Gap * 2f, y, skip, ControlHeight),
                RadioButtonStyle.Default, () => manager?.Next());
            MakeButton(content, "STOP",
                new Rect(Pad + skip * 2f + play + Gap * 3f, y, stop, ControlHeight),
                RadioButtonStyle.Default, () => manager?.Stop());
            y -= ControlHeight + Gap;

            float modeWidth = (inner - Gap * 3f) / 4f;
            shuffleButton = MakeButton(content, "SHUFFLE", new Rect(Pad, y, modeWidth, RowHeight),
                RadioButtonStyle.Toggle, () => manager?.ToggleShuffle(), out shuffleButtonLabel);
            repeatButton = MakeButton(content, "REPEAT",
                new Rect(Pad + modeWidth + Gap, y, modeWidth, RowHeight),
                RadioButtonStyle.Toggle, () => manager?.ToggleRepeat(), out repeatButtonLabel);
            MakeButton(content, "FOLDER",
                new Rect(Pad + (modeWidth + Gap) * 2f, y, modeWidth, RowHeight),
                RadioButtonStyle.Quiet, () => manager?.OpenLibraryFolder());
            MakeButton(content, "RESCAN",
                new Rect(Pad + (modeWidth + Gap) * 3f, y, modeWidth, RowHeight),
                RadioButtonStyle.Quiet, () => manager?.Rescan());
            y -= RowHeight + Space4;

            y = Heading(content, "CHANNELS", y);

            float channelsBlock = ChannelPitch * RowsPerPage;
            channelsEmptyLabel = Label(content, "",
                new Rect(Pad + Space4, y, inner - Space4 * 2f, channelsBlock),
                Dim(), FontMicro, FontStyles.Italic, TextAlignmentOptions.Center);
            channelsEmptyLabel.enableWordWrapping = true;
            channelsEmptyLabel.gameObject.SetActive(false);

            for (int i = 0; i < RowsPerPage; i++)
                rows[i] = MakeChannelRow(content, i, y - i * ChannelPitch);
            y -= channelsBlock + Space2;

            Button[] pageButtons = Stepper(
                content, y, "CHANNEL PAGE", out pageLabel, PreviousPage, NextPage);
            pagePreviousButton = pageButtons[0];
            pageNextButton = pageButtons[1];
            y -= RowHeight + Gap;

            FramedPanel(content, new Rect(Pad, y, inner, StatusHeight), Frame());
            statusLabel = Label(content, "Stand by",
                new Rect(Pad + Space2, y, inner - Space4, StatusHeight),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            statusLabel.enableWordWrapping = true;
            statusLabel.overflowMode = TextOverflowModes.Truncate;
            y -= StatusHeight;

            float panelHeight = -y + Pad;
            rootRect.sizeDelta = new Vector2(Width, panelHeight);
            Outline(content, new Rect(0f, 0f, Width, panelHeight),
                Unity(RadioUiPalette.PanelEdge));

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
            Place(rect, new Rect(Pad, y, Width - Pad * 2f, RowHeight));
            Image ground = root.GetComponent<Image>();
            Button button = root.GetComponent<Button>();
            button.targetGraphic = ground;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = ButtonColors(RadioButtonStyle.Default, false);

            var result = new ChannelRow
            {
                Root = root,
                Ground = ground,
                Button = button
            };
            Outline(rect, new Rect(0f, 0f, Width - Pad * 2f, RowHeight), Frame());
            result.SelectionRule = Rule(rect, new Rect(0f, 0f, 3f, RowHeight), Frame());
            result.BadgeGround = Panel(rect, new Rect(7f, -4f, 28f, 22f),
                new Color(0.15f, 0.35f, 0.20f, 1f));
            result.Icon = Panel(result.BadgeGround.rectTransform, new Rect(2f, -1f, 24f, 24f), Color.white);
            result.Icon.preserveAspect = true;
            result.Icon.enabled = false;
            result.Badge = Label(result.BadgeGround.rectTransform, "--", new Rect(0f, 0f, 28f, 22f),
                Color.white, FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            result.Label = Label(rect, "LOCAL",
                new Rect(43f, 0f, Width - Pad * 2f - 111f, RowHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            result.Count = Label(rect, "0",
                new Rect(Width - Pad * 2f - 62f, 0f, 52f, RowHeight),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            button.onClick.AddListener(() =>
            {
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == root)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
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

            channelLabel.text = manager.CurrentChannelCode + "  ·  " + manager.CurrentChannelName;
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
            shuffleButtonLabel.color = manager.Shuffle ? Color.white : Accent();
            repeatButtonLabel.color = manager.RepeatTrack ? Color.white : Accent();
            SetLatched(shuffleButton, RadioButtonStyle.Toggle, manager.Shuffle);
            SetLatched(repeatButton, RadioButtonStyle.Toggle, manager.RepeatTrack);
            statusLabel.text = manager.Status;
            pageLabel.text = (page + 1) + " / " + pages;
            pagePreviousButton.interactable = page > 0;
            pageNextButton.interactable = page + 1 < pages;

            // An empty channel list reads as a table still loading; say what to do instead.
            bool noChannels = manager.ChannelCount == 0;
            if (channelsEmptyLabel != null)
            {
                if (channelsEmptyLabel.gameObject.activeSelf != noChannels)
                    channelsEmptyLabel.gameObject.SetActive(noChannels);
                if (noChannels)
                    channelsEmptyLabel.text =
                        "No music folders found. Press FOLDER to open the library, add " +
                        "subfolders of audio, then press RESCAN.";
            }

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
                bool selected = index == manager.SelectedChannel;
                item.Label.color = selected ? Color.white : Friendly();
                item.SelectionRule.color = selected ? Accent() : Frame();
                item.Button.colors = ButtonColors(RadioButtonStyle.Default, selected);
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

        private static Button MakeButton(
            RectTransform parent, string text, Rect rect, RadioButtonStyle style, Action action) =>
            MakeButton(parent, text, rect, style, action, out _);

        private static Button MakeButton(
            RectTransform parent, string text, Rect rect, RadioButtonStyle style,
            Action action, out TMP_Text label)
        {
            var root = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, rect);
            Image image = root.GetComponent<Image>();
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = ButtonColors(style, false);
            button.onClick.AddListener(() =>
            {
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == root)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
                action?.Invoke();
                nextRefresh = 0f;
            });
            Outline(rt, new Rect(0f, 0f, rect.width, rect.height),
                style == RadioButtonStyle.Quiet ? Frame() : Accent().WithAlpha(0.80f));
            RadioUiPaint paint = RadioUiPalette.Paint(
                style, Radio(Accent()), true, false, false, false);
            label = Label(rt, text, new Rect(0f, 0f, rect.width, rect.height),
                Unity(paint.Text), FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);
            return button;
        }

        private static ColorBlock ButtonColors(RadioButtonStyle style, bool selected)
        {
            RadioRgba accent = Radio(Accent());
            return new ColorBlock
            {
                normalColor = Unity(RadioUiPalette.Paint(
                    style, accent, true, selected, false, false).Fill),
                highlightedColor = Unity(RadioUiPalette.Paint(
                    style, accent, true, selected, true, false).Fill),
                pressedColor = Unity(RadioUiPalette.Paint(
                    style, accent, true, selected, true, true).Fill),
                selectedColor = Unity(RadioUiPalette.Paint(
                    style, accent, true, selected, true, false).Fill),
                disabledColor = Unity(RadioUiPalette.Paint(
                    style, accent, false, false, false, false).Fill),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
        }

        private static void SetLatched(Button button, RadioButtonStyle style, bool selected)
        {
            if (button != null) button.colors = ButtonColors(style, selected);
        }

        private static float Heading(RectTransform parent, string text, float y)
        {
            // The rule starts after the text's measured width, not a fixed 78px that only
            // happened to fit "CHANNELS". Returns the cursor advanced past the heading.
            TMP_Text label = Label(parent, text, new Rect(Pad, y, Width - Pad * 2f, Space4),
                Friendly(), FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            float labelWidth = Mathf.Ceil(label.GetPreferredValues(text).x);

            float ruleX = Pad + labelWidth + Space2;
            Rule(parent, new Rect(ruleX, y - Space2,
                Mathf.Max(0f, Width - Pad - ruleX), 1f), Frame());
            return y - Space6;
        }

        private static Button[] Stepper(
            RectTransform parent, float y, string name, out TMP_Text value,
            Action previous, Action next, string previousText = "<", string nextText = ">")
        {
            const float gutter = 86f;
            const float arrow = 34f;
            Label(parent, name, new Rect(Pad, y, gutter - Gap, RowHeight),
                Dim(), FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            float x = Pad + gutter;
            float width = Width - Pad - x;
            FramedPanel(parent, new Rect(x, y, width, RowHeight), Frame());
            Button previousButton = MakeButton(
                parent, previousText, new Rect(x + 1f, y - 1f, arrow, RowHeight - 2f),
                RadioButtonStyle.Quiet, previous);
            Button nextButton = MakeButton(parent, nextText,
                new Rect(x + width - arrow - 1f, y - 1f, arrow, RowHeight - 2f),
                RadioButtonStyle.Quiet, next);
            value = Label(parent, "",
                new Rect(x + arrow + Gap, y, width - (arrow + Gap) * 2f, RowHeight),
                Friendly(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);
            return new[] { previousButton, nextButton };
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
            label.overflowMode = TextOverflowModes.Ellipsis;
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

        private static Image FramedPanel(RectTransform parent, Rect rect, Color frame)
        {
            Image fill = Panel(parent, rect, Unity(RadioUiPalette.SurfaceCard));
            Outline(parent, rect, frame);
            return fill;
        }

        private static void Outline(RectTransform parent, Rect rect, Color color)
        {
            const float thickness = 1f;
            Rule(parent, new Rect(rect.x, rect.y, rect.width, thickness), color);
            Rule(parent, new Rect(rect.x, rect.y - rect.height + thickness,
                rect.width, thickness), color);
            Rule(parent, new Rect(rect.x, rect.y, thickness, rect.height), color);
            Rule(parent, new Rect(rect.x + rect.width - thickness, rect.y,
                thickness, rect.height), color);
        }

        private static Image Rule(RectTransform parent, Rect rect, Color color) => Panel(parent, rect, color);

        private static (Image Background, TMP_Text Label) StatusChip(
            RectTransform parent, string text, Rect rect, Color railColor, Color textColor, float fontSize = FontMicro)
        {
            var go = new GameObject("StatusChip", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, rect);

            Image bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            bg.color = new Color(railColor.r * 0.15f, railColor.g * 0.15f, railColor.b * 0.15f, 0.85f);

            Outline(parent, rect, new Color(railColor.r, railColor.g, railColor.b, 0.45f));

            var lblGo = new GameObject("ChipLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lRt = lblGo.GetComponent<RectTransform>();
            lRt.SetParent(parent, false);
            Place(lRt, rect);
            var label = lblGo.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = textColor;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return (bg, label);
        }

        private static Image TacticalCard(RectTransform parent, Rect area, Color railColor)
        {
            FramedPanel(parent, area, Unity(RadioUiPalette.BorderSubtle));
            return Rule(parent, new Rect(area.x, area.y, 3f, area.height), railColor);
        }

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

        private static Color Dim() => Unity(RadioUiPalette.Dim);

        private static Color Frame() => Unity(RadioUiPalette.Frame);

        private static RadioRgba Radio(Color color) =>
            new RadioRgba(color.r, color.g, color.b, color.a);

        private static Color Unity(RadioRgba color) =>
            new Color(color.R, color.G, color.B, color.A);

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
