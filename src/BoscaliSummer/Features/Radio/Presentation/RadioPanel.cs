using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
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
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float Pad = AvTokens.Pad;
        private const float Gap = AvTokens.Gap;
        private const float RowHeight = AvTokens.RowHeight;
        private const float ControlHeight = 34f;
        private const float CardHeight = 94f;
        private const float ArtSize = 60f;
        private const float ChannelPitch = RowHeight + 2f;
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
            public AvButton Button;
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
        private static AvButton pagePreviousButton;
        private static AvButton pageNextButton;
        private static AvStyled.DataBar dataBar;
        private static TMP_Text statusLabel;
        private static TMP_Text channelsEmptyLabel;
        private static Image progressFill;
        private static AvButton playButton;
        private static AvButton shuffleButton;
        private static AvButton repeatButton;
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
            playButton = null;
            shuffleButton = null;
            repeatButton = null;
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
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
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
            // Position is deliberately not copied. VirtualMFD.showPos is Vector3.zero and
            // MFDScreen.ShowScreen assigns it straight to localPosition, so a screen has no
            // remembered home — it is placed by its parent and anchors, and an
            // anchoredPosition written here is overwritten whenever the panel is opened.
            rootRect.sizeDelta = new Vector2(Width, PanelHeight);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            // The hard data bar: a filled id tag, the live state, and three chips, in one
            // 30px row. It replaces the centred title, the subtitle and the separate chip
            // rail these panels used to stack, which cost about 24px of height and said
            // nothing the id tag does not.
            float inner = Width - Pad * 2f;
            var bar = new Rect(Pad, -Pad, inner, AvTokens.TitleBarHeight + 2f);
            dataBar = AvStyled.TopBar(content, bar, "RAD", 3);

            float y = bar.y - bar.height - AvTokens.Space2;

            // The spine every section hangs off, in place of a frame around each one.
            float bodyTop = y;
            float bodyBottom = -(PanelHeight - Pad - AvTokens.StatusStripHeight - AvTokens.Space2);
            AvStyled.Spine(content, new Rect(Pad, bodyTop, 3f, bodyTop - bodyBottom));

            float spineInset = 14f;
            float bodyX = Pad + spineInset;
            float bodyW = Width - Pad - bodyX;

            // Now-playing card: station art on the left, the signal line, the track title,
            // and a progress bar. Its internals stay positioned against the card top.
            float cardTop = y;
            var (cardFill, rail) = AvKit.TacticalCard(content, new Rect(bodyX, cardTop, bodyW, CardHeight), AvTheme.RailReady);
            stationIconGround = AvKit.Panel(content,
                new Rect(bodyX + AvTokens.Space2, cardTop - AvTokens.Space2, ArtSize, ArtSize), AvTheme.SurfaceInert, AvSprites.Card);
            AvKit.Outline(content, new Rect(bodyX + AvTokens.Space2, cardTop - AvTokens.Space2, ArtSize, ArtSize), AvTheme.Frame);

            stationIcon = AvKit.Panel(stationIconGround.rectTransform,
                new Rect(AvTokens.Space1 + 1f, -(AvTokens.Space1 + 1f), ArtSize - AvTokens.Space2 - 2f, ArtSize - AvTokens.Space2 - 2f),
                Color.white);
            stationIcon.preserveAspect = true;
            stationIcon.enabled = false;
            stationBadge = AvKit.Label(stationIconGround.rectTransform, "--",
                new Rect(0f, 0f, ArtSize, ArtSize),
                Color.white, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.Center);

            float infoX = bodyX + AvTokens.Space2 + ArtSize + AvTokens.Space3;
            float infoW = bodyX + bodyW - infoX;
            channelLabel = AvKit.Label(content, "",
                new Rect(infoX, cardTop - AvTokens.Space2, infoW, AvTokens.Space4),
                AvTheme.Friendly, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            trackLabel = AvKit.Label(content, "NO LOCAL TRACKS",
                new Rect(infoX, cardTop - AvTokens.Space5 - AvTokens.Space2, infoW, AvTokens.Space6 + AvTokens.Space2),
                Color.white, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            progressFill = AvKit.ProgressBar(content,
                new Rect(infoX, cardTop - CardHeight + AvTokens.Space6, infoW, 4f),
                0f, AvTheme.Accent);

            timeLabel = AvKit.Label(content, "00:00 / 00:00",
                new Rect(infoX, cardTop - CardHeight + AvTokens.Space4, infoW, AvTokens.Space4),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);

            y = cardTop - CardHeight - AvTokens.Space3;

            // Transport
            const float skip = 90f;
            const float play = 140f;
            float stop = bodyW - skip * 2f - play - Gap * 3f;
            AvKit.Button(content, "PREV", new Rect(bodyX, y, skip, ControlHeight),
                () => manager?.Previous(), AvTokens.FontSmall, AvButtonStyle.Default);
            playButton = AvKit.Button(content, "PLAY", new Rect(bodyX + skip + Gap, y, play, ControlHeight),
                () => manager?.TogglePlayback(), AvTokens.FontSmall, AvButtonStyle.Primary);
            AvKit.Button(content, "NEXT", new Rect(bodyX + skip + play + Gap * 2f, y, skip, ControlHeight),
                () => manager?.Next(), AvTokens.FontSmall, AvButtonStyle.Default);
            AvKit.Button(content, "STOP", new Rect(bodyX + skip * 2f + play + Gap * 3f, y, stop, ControlHeight),
                () => manager?.Stop(), AvTokens.FontSmall, AvButtonStyle.Default);
            y -= ControlHeight + Gap;

            float modeWidth = (bodyW - Gap * 3f) / 4f;
            shuffleButton = AvKit.Button(content, "SHUFFLE", new Rect(bodyX, y, modeWidth, RowHeight),
                () => manager?.ToggleShuffle(), AvTokens.FontMicro, AvButtonStyle.Toggle);
            repeatButton = AvKit.Button(content, "REPEAT", new Rect(bodyX + modeWidth + Gap, y, modeWidth, RowHeight),
                () => manager?.ToggleRepeat(), AvTokens.FontMicro, AvButtonStyle.Toggle);
            AvKit.Button(content, "FOLDER", new Rect(bodyX + (modeWidth + Gap) * 2f, y, modeWidth, RowHeight),
                () => manager?.OpenLibraryFolder(), AvTokens.FontMicro, AvButtonStyle.Quiet);
            AvKit.Button(content, "RESCAN", new Rect(bodyX + (modeWidth + Gap) * 3f, y, modeWidth, RowHeight),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet);
            y -= RowHeight + AvTokens.Space4;

            AvStyled.SpineTick(content, Pad + 3f, y - 8f);
            AvStyled.Label(content, new Rect(bodyX, y, bodyW, 14f), "CHANNELS", "section-title");
            AvStyled.Label(content, new Rect(bodyX, y, bodyW, 14f), "VHF-COM", "section-title-note",
                           align: TextAlignmentOptions.MidlineRight);
            y -= 20f;

            float channelsBlock = ChannelPitch * RowsPerPage;
            channelsEmptyLabel = AvKit.Label(content, "",
                new Rect(bodyX + AvTokens.Space4, y, bodyW - AvTokens.Space4 * 2f, channelsBlock),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Italic, TextAlignmentOptions.Center, wrap: true);
            channelsEmptyLabel.gameObject.SetActive(false);

            for (int i = 0; i < RowsPerPage; i++)
                rows[i] = MakeChannelRow(content, i, y - i * ChannelPitch);
            y -= channelsBlock + AvTokens.Space2;

            AvButton[] pageButtons = AvKit.Stepper(
                content, bodyX, y, bodyW, out pageLabel, PreviousPage, NextPage);
            pagePreviousButton = pageButtons[0];
            pageNextButton = pageButtons[1];
            y -= RowHeight + Gap;

            // Pinned Status Strip at footer
            float terminalY = -(PanelHeight - Pad - AvTokens.StatusStripHeight);
            statusLabel = AvStyled.StatusStrip(content, new Rect(Pad, terminalY, inner, AvTokens.StatusStripHeight));

            // Chamfer Corner Ticks
            AvKit.CornerTicks(content, new Rect(0f, 0f, Width, PanelHeight), AvTheme.Hairline);

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
            // A channel row is a list entry, not an object: a hairline underneath and a
            // selection fill, rather than the outline-plus-corner-ticks card every group
            // used to wear. Ticks now mean "this is the focused thing", so spending them
            // on five identical rows made them mean nothing.
            const float inset = 14f;
            float x = Pad + inset;
            float w = Width - Pad - x;

            Image ground = AvKit.Panel(parent, new Rect(x, y, w, RowHeight), Color.clear);
            AvKit.Rule(parent, new Rect(x, y - RowHeight, w, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
            Image selectionRule = AvKit.Rule(parent, new Rect(x, y, 3f, RowHeight), Color.clear);

            RectTransform rect = ground.rectTransform;

            var result = new ChannelRow
            {
                Root = ground.gameObject,
                Ground = ground,
                SelectionRule = selectionRule
            };

            result.BadgeGround = AvKit.Panel(rect, new Rect(10f, -4f, 26f, 22f), AvTheme.SurfaceInert);
            result.Icon = AvKit.Panel(result.BadgeGround.rectTransform, new Rect(1f, -1f, 24f, 24f), Color.white);
            result.Icon.preserveAspect = true;
            result.Icon.enabled = false;
            result.Badge = AvKit.Label(result.BadgeGround.rectTransform, "--", new Rect(0f, 0f, 26f, 22f),
                Color.white, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            result.Label = AvStyled.Label(rect, new Rect(44f, 0f, w - 112f, RowHeight), "LOCAL", "row-name");
            result.Count = AvStyled.Label(rect, new Rect(w - 62f, 0f, 52f, RowHeight), "0", "row-sub",
                                          align: TextAlignmentOptions.MidlineRight);

            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, w, RowHeight), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && result.Index >= 0) manager.SelectChannel(result.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear,
                AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised));
            result.Button = button;
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
            ApplyStationIcon(
                stationIcon, stationBadge, manager.GetChannelIconPath(manager.SelectedChannel),
                manager.CurrentChannelCode);
            trackLabel.text = manager.CurrentTrackTitle;
            progressFill.fillAmount = manager.Progress;
            timeLabel.text = FormatTime(manager.Elapsed) + " / " + FormatTime(manager.Duration);
            playButton?.SetText(manager.IsPaused ? "RESUME" : manager.IsEngaged ? "PAUSE" : "PLAY");
            shuffleButton?.SetText(manager.Shuffle ? "SHUFFLE ON" : "SHUFFLE");
            repeatButton?.SetText(manager.RepeatTrack ? "REPEAT ON" : "REPEAT");
            shuffleButton?.SetLatched(manager.Shuffle);
            repeatButton?.SetLatched(manager.RepeatTrack);
            statusLabel.text = "> " + manager.Status;
            pageLabel.text = (page + 1) + " / " + pages;
            pagePreviousButton?.SetEnabled(page > 0);
            pageNextButton?.SetEnabled(page + 1 < pages);

            if (dataBar != null)
            {
                bool playing = manager.IsEngaged && !manager.IsPaused;
                bool haveTracks = manager.ChannelCount > 0;
                dataBar.State.text = playing ? "RECEIVING"
                                    : manager.IsPaused ? "PAUSED"
                                    : haveTracks ? "STANDBY" : "NO LIBRARY";
                dataBar.State.color = playing ? AvTheme.RailReady
                                    : haveTracks ? AvTheme.Dim : AvTheme.Warning;
                dataBar.SetChip(0, "AUDIO-NET", haveTracks);
                dataBar.SetChip(1, playing ? "SIGNAL HI" : "SIGNAL --", playing);
                dataBar.SetChip(2, "VHF-COM", true);
            }

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
                item.Label.color = selected ? Color.white : AvTheme.TextPrimary;
                item.SelectionRule.color = selected ? AvTheme.Accent : Color.clear;
                item.Ground.color = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear;
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

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        private static string FormatTime(float seconds)
        {
            int value = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (value / 60).ToString("00") + ":" + (value % 60).ToString("00");
        }

        private static void Fail(string reason)
        {
            gaveUp = true;
            screen = null;
            Plugin.Logger.LogWarning("Radio panel disabled (" + reason + "). Playback remains available through config reload only.");
        }
    }
}
