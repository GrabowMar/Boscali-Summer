using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Radio.Runtime;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>
    /// The local deck: the player's own folders, browsed and played from the map. It shares
    /// the receiver's audio path and its hold on the vanilla soundtrack, but it has nothing
    /// to do with the dial — no frequencies, no squelch, no dead air. Music you own, played
    /// when you want it.
    ///
    /// <para>Hosted rather than claimed: the six vanilla bezel slots are already spoken for,
    /// so the MUS button is appended to the vanilla column like EVN and ADM.</para>
    /// </summary>
    internal static class MusicPanel
    {
        private const float Width = AvTokens.PanelWidth;
        private const float Pad = AvTokens.Pad;
        private const float Gap = AvTokens.Gap;
        private const float HeroHeight = 104f;
        private const float ControlHeight = 32f;
        private const float KeyHeight = 30f;
        private const float HeaderHeight = 16f;
        private const float FolderPitch = 30f;
        private const float TrackLinePitch = 15f;
        private const float TrackLineHeight = 14f;
        private const float ContentHeight = 580f;
        private const int FolderRows = 4;
        private const int TrackRows = 6;

        private sealed class FolderRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image SelectionRule;
            public TMP_Text Name;
            public TMP_Text Count;
        }

        private sealed class TrackRow
        {
            public int Index = -1;
            public GameObject Root;
            public Image Ground;
            public Image Rule;
            public TMP_Text Number;
            public TMP_Text Title;
        }

        private static readonly FolderRow[] folderRows = new FolderRow[FolderRows];
        private static readonly TrackRow[] trackRows = new TrackRow[TrackRows];

        private static MFDScreen screen;
        private static GameObject screenRoot;
        private static AvScreen shell;
        private static RectTransform pageRoot;
        private static RadioManager manager;
        private static TMP_Text folderLabel;
        private static TMP_Text trackTitleLabel;
        private static TMP_Text timeLabel;
        private static TMP_Text folderNote;
        private static TMP_Text trackNote;
        private static TMP_Text emptyLabel;
        private static TMP_Text footerLabel;
        private static Image progressFill;
        private static AvButton playButton;
        private static AvButton shuffleButton;
        private static AvButton repeatButton;
        private static AvButton volumeDownButton;
        private static AvButton volumeUpButton;
        private static AvButton folderPreviousButton;
        private static AvButton folderNextButton;
        private static AvButton trackPreviousButton;
        private static AvButton trackNextButton;
        private static int folderPage;
        private static int trackPage;
        private static float nextAttempt;
        private static float nextRefresh;
        private static bool failed;
        private static bool unavailableLogged;

        public static void Tick(RadioManager radio)
        {
            manager = radio;
            if (failed) return;
            if (!GameAccess.MfdAvailable)
            {
                if (!unavailableLogged)
                {
                    unavailableLogged = true;
                    Plugin.Logger.LogWarning(
                        "Music deck panel unavailable: VirtualMFD access did not resolve.");
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

            if (!screen.isActive) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.15f;
            Refresh();
        }

        public static void Reset()
        {
            MfdScreenHost.Release(MfdSlots.Mus);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            shell = null;
            pageRoot = null;
            manager = null;
            folderLabel = null;
            trackTitleLabel = null;
            timeLabel = null;
            folderNote = null;
            trackNote = null;
            emptyLabel = null;
            footerLabel = null;
            progressFill = null;
            playButton = null;
            shuffleButton = null;
            repeatButton = null;
            volumeDownButton = null;
            volumeUpButton = null;
            folderPreviousButton = null;
            folderNextButton = null;
            trackPreviousButton = null;
            trackNextButton = null;
            for (int i = 0; i < folderRows.Length; i++) folderRows[i] = null;
            for (int i = 0; i < trackRows.Length; i++) trackRows[i] = null;
            folderPage = 0;
            trackPage = 0;
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private static void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdScreenHost.TryHost(MfdSlots.Mus, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    Plugin.Logger.LogWarning("MUS MFD unavailable: could not add a host button.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdScreenHost.Release(MfdSlots.Mus);
                    return;
                }

                if (!RadioScreens.TryBuild(template, buttons[slot], MfdSlots.Mus, "BoscaliMusic.Screen",
                    ContentHeight, () => nextRefresh = 0f, out RadioScreen built))
                {
                    MfdScreenHost.Release(MfdSlots.Mus);
                    failed = true;
                    return;
                }

                screen = built.Screen;
                screenRoot = built.Root;
                shell = built.Shell;
                pageRoot = built.Page;

                var cursor = new RadioCursor(built.Area);
                BuildHero(cursor.Take(HeroHeight));
                BuildTransport(ref cursor);
                BuildFolders(ref cursor);
                BuildTracks(ref cursor);
                BuildFooter(ref cursor);

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    Reset();
                    failed = true;
                    Plugin.Logger.LogWarning("MUS MFD unavailable: bezel changed during installation.");
                    return;
                }

                Refresh();
                Plugin.Logger.LogInfo("Music deck MFD installed on " + (left ? "left" : "right") +
                    " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                Reset();
                failed = true;
                Plugin.Logger.LogError("MUS MFD install failed: " + e);
            }
        }

        private static void BuildHero(Rect area)
        {
            AvKit.TacticalCard(pageRoot, area, AvTheme.RailInfo);

            folderLabel = AvStyled.Label(pageRoot,
                new Rect(area.x + AvTokens.Space3, area.y - AvTokens.Space2, area.width - AvTokens.Space6, 14f),
                "LOCAL FOLDERS", "section-title");
            trackTitleLabel = AvStyled.Label(pageRoot,
                new Rect(area.x + AvTokens.Space3, area.y - 30f, area.width - AvTokens.Space6 - 96f, 20f),
                "NO TRACK", "row-main");
            timeLabel = AvKit.Label(pageRoot, "--:-- / --:--",
                new Rect(area.x + area.width - AvTokens.Space3 - 92f, area.y - 30f, 92f, 20f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            progressFill = AvKit.ProgressBar(pageRoot,
                new Rect(area.x + AvTokens.Space3, area.y - 60f, area.width - AvTokens.Space6, 6f),
                0f, AvTheme.RailInfo);

            emptyLabel = AvKit.Label(pageRoot, "",
                new Rect(area.x + AvTokens.Space3, area.y - 78f, area.width - AvTokens.Space6, 16f),
                AvTheme.Warning, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            emptyLabel.gameObject.SetActive(false);
        }

        private static void BuildTransport(ref RadioCursor cursor)
        {
            Rect row = cursor.Take(ControlHeight, 6f);
            float keyWidth = (row.width - Gap * 3f) / 4f;
            float x = row.x;
            AvKit.Button(pageRoot, "|<", new Rect(x, row.y, keyWidth, ControlHeight),
                () => manager?.DeckPrevious(), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Previous track in this folder.");
            playButton = AvKit.Button(pageRoot, "PLAY", new Rect(x += keyWidth + Gap, row.y, keyWidth, ControlHeight),
                () => manager?.DeckTogglePlayback(), AvTokens.FontSmall, AvButtonStyle.Primary)
                .WithTooltip("Play or pause the selected track.");
            AvKit.Button(pageRoot, ">|", new Rect(x += keyWidth + Gap, row.y, keyWidth, ControlHeight),
                () => manager?.DeckNext(), AvTokens.FontSmall, AvButtonStyle.Quiet)
                .WithTooltip("Next track in this folder.");
            AvKit.Button(pageRoot, "STOP", new Rect(x += keyWidth + Gap, row.y, keyWidth, ControlHeight),
                () => manager?.DeckStop(), AvTokens.FontSmall, AvButtonStyle.Danger)
                .WithTooltip("Stop the deck and release the soundtrack bus if the receiver is off.");

            Rect keyRow = cursor.Take(KeyHeight, AvTokens.Space2);
            float keyWidth2 = (keyRow.width - Gap * 5f) / 6f;
            x = keyRow.x;
            shuffleButton = AvKit.Button(pageRoot, "SHUF", new Rect(x, keyRow.y, keyWidth2, KeyHeight),
                () => manager?.DeckToggleShuffle(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Random track order, for the deck and the radio programme.");
            repeatButton = AvKit.Button(pageRoot, "REP", new Rect(x += keyWidth2 + Gap, keyRow.y, keyWidth2, KeyHeight),
                () => manager?.DeckToggleRepeat(), AvTokens.FontMicro, AvButtonStyle.Toggle)
                .WithTooltip("Repeat the current track instead of advancing.");
            volumeDownButton = AvKit.Button(pageRoot, "AF-", new Rect(x += keyWidth2 + Gap, keyRow.y, keyWidth2, KeyHeight),
                () => manager?.NudgeVolume(-0.1f), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Turn the shared radio/deck volume down.");
            volumeUpButton = AvKit.Button(pageRoot, "AF+", new Rect(x += keyWidth2 + Gap, keyRow.y, keyWidth2, KeyHeight),
                () => manager?.NudgeVolume(0.1f), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Turn the shared radio/deck volume up.");
            AvKit.Button(pageRoot, "FOLDER", new Rect(x += keyWidth2 + Gap, keyRow.y, keyWidth2, KeyHeight),
                () => manager?.OpenLibraryFolder(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Open Boscali Summer's local music library folder.");
            AvKit.Button(pageRoot, "RESCAN", new Rect(x += keyWidth2 + Gap, keyRow.y, keyWidth2, KeyHeight),
                () => manager?.Rescan(), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Rescan the local music library for new folders and tracks.");
        }

        private static void BuildFolders(ref RadioCursor cursor)
        {
            Rect header = cursor.Take(HeaderHeight, 6f);
            SectionTitle(header.x, header.width, header.y, "FOLDERS");
            folderPreviousButton = AvKit.Button(pageRoot, "<",
                new Rect(header.x + header.width - 88f, header.y - 1f, 22f, 16f),
                () => NudgeFolderPage(-1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Show the previous page of folders.");
            folderNote = AvStyled.Label(pageRoot,
                new Rect(header.x + header.width - 64f, header.y, 40f, HeaderHeight),
                "0 / 0", "section-title-note", align: TextAlignmentOptions.Center);
            folderNextButton = AvKit.Button(pageRoot, ">",
                new Rect(header.x + header.width - 22f, header.y - 1f, 22f, 16f),
                () => NudgeFolderPage(1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Show the next page of folders.");

            float top = cursor.Y;
            Rect frame = new Rect(cursor.X, top, cursor.Width, FolderPitch);
            for (int i = 0; i < FolderRows; i++)
                folderRows[i] = MakeFolderRow(frame, top - i * FolderPitch);
            cursor.Skip(FolderPitch * FolderRows + AvTokens.Space2);
        }

        private static void BuildTracks(ref RadioCursor cursor)
        {
            Rect header = cursor.Take(HeaderHeight, 6f);
            SectionTitle(header.x, header.width, header.y, "TRACKS");
            trackPreviousButton = AvKit.Button(pageRoot, "<",
                new Rect(header.x + header.width - 88f, header.y - 1f, 22f, 16f),
                () => NudgeTrackPage(-1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Show the previous page of tracks.");
            trackNote = AvStyled.Label(pageRoot,
                new Rect(header.x + header.width - 64f, header.y, 40f, HeaderHeight),
                "0 / 0", "section-title-note", align: TextAlignmentOptions.Center);
            trackNextButton = AvKit.Button(pageRoot, ">",
                new Rect(header.x + header.width - 22f, header.y - 1f, 22f, 16f),
                () => NudgeTrackPage(1), AvTokens.FontMicro, AvButtonStyle.Quiet)
                .WithTooltip("Show the next page of tracks.");

            float top = cursor.Y;
            Rect frame = new Rect(cursor.X, top, cursor.Width, TrackLinePitch);
            for (int i = 0; i < TrackRows; i++)
                trackRows[i] = MakeTrackRow(frame, top - i * TrackLinePitch);
            cursor.Skip(TrackLinePitch * TrackRows + AvTokens.Space2);
        }

        private static void BuildFooter(ref RadioCursor cursor)
        {
            Rect area = cursor.Take(AvTokens.Space6);
            footerLabel = AvKit.Label(pageRoot,
                "LOCAL LIBRARY ONLY · OGG/WAV · NOTHING IS DOWNLOADED, BUNDLED OR SENT",
                new Rect(area.x, area.y, area.width, 20f),
                AvTheme.Disabled, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        }

        private static void SectionTitle(float x, float width, float y, string title)
        {
            TMP_Text label = AvStyled.Label(pageRoot, new Rect(x, y, 160f, HeaderHeight), title, "section-title");
            float titleWidth = Mathf.Ceil(label.GetPreferredValues(title).x);
            const float tickWidth = 20f;
            AvKit.Rule(pageRoot, new Rect(x + titleWidth + AvTokens.Space2, y - 7f,
                Mathf.Min(tickWidth, Mathf.Max(0f, width - titleWidth - AvTokens.Space2)), 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));
        }

        private static FolderRow MakeFolderRow(Rect frame, float y)
        {
            Image ground = AvKit.Panel(pageRoot, new Rect(frame.x, y, frame.width, FolderPitch - 2f), Color.clear);
            RectTransform rect = ground.rectTransform;
            AvKit.Rule(rect, new Rect(0f, -(FolderPitch - 2f), rect.sizeDelta.x, 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
            Image selectionRule = AvKit.Rule(rect, new Rect(0f, 0f, 3f, FolderPitch - 2f), Color.clear);

            var result = new FolderRow
            {
                Root = ground.gameObject,
                Ground = ground,
                SelectionRule = selectionRule
            };
            result.Name = AvStyled.Label(rect, new Rect(12f, 0f, rect.sizeDelta.x - 80f, FolderPitch - 2f),
                "FOLDER", "row-name");
            result.Count = AvStyled.Label(rect, new Rect(rect.sizeDelta.x - 68f, 0f, 60f, FolderPitch - 2f),
                "0", "row-sub", align: TextAlignmentOptions.Right);

            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, rect.sizeDelta.x, FolderPitch - 2f), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && result.Index >= 0) manager.DeckSelectFolder(result.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear,
                AvStyleHost.Resolve(AvStyleHost.Style("row", "hover").Background, AvTheme.SurfaceRaised));
            button.WithTooltip("Browse this folder in the track list below.");
            return result;
        }

        private static TrackRow MakeTrackRow(Rect frame, float y)
        {
            float width = frame.width;
            Image ground = AvKit.Panel(pageRoot, new Rect(frame.x, y, width, TrackLineHeight), Color.clear);
            RectTransform rect = ground.rectTransform;
            Image rule = AvKit.Rule(rect, new Rect(0f, 0f, 3f, TrackLineHeight), Color.clear);

            var result = new TrackRow
            {
                Root = ground.gameObject,
                Ground = ground,
                Rule = rule
            };
            result.Number = AvKit.Label(rect, "1", new Rect(8f, 0f, 26f, TrackLineHeight),
                AvTheme.Disabled, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            result.Title = AvStyled.Label(rect, new Rect(40f, 0f, width - 48f, TrackLineHeight), "", "row-sub");

            AvButton button = AvKit.HitButton(rect, new Rect(0f, 0f, width, TrackLineHeight), () =>
            {
                AvInput.Deselect(ground.gameObject);
                if (manager != null && result.Index >= 0) manager.DeckPlay(result.Index);
                nextRefresh = 0f;
            });
            button.SetRowHighlight(ground, Color.clear, AvTheme.Unity(AvTokens.Wash(
                AvTheme.RailInfo.ToRgba(), AvTokens.RowHoverScale, AvTokens.RowHoverAlpha)));
            button.WithTooltip("Play this track on the local deck.");
            return result;
        }

        private static void NudgeFolderPage(int direction)
        {
            if (manager == null) return;
            int pages = Math.Max(1, (manager.DeckFolderCount + FolderRows - 1) / FolderRows);
            folderPage = Mathf.Clamp(folderPage + direction, 0, pages - 1);
            nextRefresh = 0f;
        }

        private static void NudgeTrackPage(int direction)
        {
            if (manager == null) return;
            int pages = Math.Max(1, (manager.DeckTrackCount + TrackRows - 1) / TrackRows);
            trackPage = Mathf.Clamp(trackPage + direction, 0, pages - 1);
            nextRefresh = 0f;
        }

        private static void Refresh()
        {
            if (manager == null || folderLabel == null) return;

            int folderCount = manager.DeckFolderCount;
            bool hasFolders = folderCount > 0;
            bool playing = manager.DeckEngaged;

            folderLabel.text = hasFolders
                ? manager.DeckFolderName(manager.DeckFolder)
                : "LOCAL FOLDERS";
            trackTitleLabel.text = manager.DeckCurrentTitle;
            timeLabel.text = "--:-- / --:--";
            progressFill.fillAmount = 0f;

            if (playing)
            {
                float elapsed = manager.DeckElapsed;
                float duration = manager.DeckDuration;
                progressFill.fillAmount = manager.DeckProgress;
                timeLabel.text = FormatTime(elapsed) + " / " + FormatTime(duration);
            }

            emptyLabel.gameObject.SetActive(!hasFolders);
            if (!hasFolders)
                emptyLabel.text = "No folders with OGG/WAV files yet. Press FOLDER, add some, then RESCAN.";

            playButton?.SetText(manager.DeckPaused ? "RESUME" : manager.DeckPlaying ? "PAUSE" : hasFolders ? "PLAY" : "NO LIB");
            playButton?.SetEnabled(hasFolders);
            shuffleButton?.SetLatched(manager.Shuffle);
            repeatButton?.SetLatched(manager.RepeatTrack);
            volumeDownButton?.SetEnabled(manager.VolumeLevel > 0.001f);
            volumeUpButton?.SetEnabled(manager.VolumeLevel < 0.999f);
            footerLabel.text = manager.DeckStatus;
            shell?.WriteStatus(null, MapPicker.Prompt, manager.DeckStatus);

            int folderPages = Math.Max(1, (folderCount + FolderRows - 1) / FolderRows);
            folderPage = Mathf.Clamp(folderPage, 0, folderPages - 1);
            folderNote.text = hasFolders ? (folderPage + 1) + " / " + folderPages : "0 / 0";
            folderPreviousButton?.SetEnabled(folderPage > 0);
            folderNextButton?.SetEnabled(folderPage + 1 < folderPages);

            for (int i = 0; i < FolderRows; i++)
            {
                FolderRow row = folderRows[i];
                if (row == null) continue;
                int index = folderPage * FolderRows + i;
                row.Index = index < folderCount ? index : -1;
                if (row.Root.activeSelf != (row.Index >= 0)) row.Root.SetActive(row.Index >= 0);
                if (row.Index < 0) continue;

                bool selected = index == manager.DeckFolder;
                int count = manager.DeckFolderTrackCount(index);
                row.Name.text = manager.DeckFolderName(index);
                row.Count.text = count + " TRK";
                row.Name.color = selected ? AvTheme.Accent : AvTheme.TextPrimary;
                row.Count.color = selected ? AvTheme.Accent : AvTheme.Unity(AvTokens.TextDim);
                row.SelectionRule.color = selected ? AvTheme.RailInfo : Color.clear;
                row.Ground.color = selected
                    ? AvTheme.Unity(AvTokens.Wash(AvTheme.RailInfo.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear;
            }

            int trackCount = manager.DeckTrackCount;
            int trackPages = Math.Max(1, (trackCount + TrackRows - 1) / TrackRows);
            trackPage = Mathf.Clamp(trackPage, 0, trackPages - 1);
            trackNote.text = trackCount == 0 ? "NO TRACKS" : (trackPage + 1) + " / " + trackPages;
            trackPreviousButton?.SetEnabled(trackPage > 0);
            trackNextButton?.SetEnabled(trackPage + 1 < trackPages);

            int current = manager.DeckTrackIndex;
            for (int i = 0; i < TrackRows; i++)
            {
                TrackRow row = trackRows[i];
                if (row == null) continue;
                int index = trackPage * TrackRows + i;
                row.Index = index < trackCount ? index : -1;
                if (row.Root.activeSelf != (row.Index >= 0)) row.Root.SetActive(row.Index >= 0);
                if (row.Index < 0) continue;

                bool active = index == current && manager.DeckEngaged;
                row.Number.text = (index + 1).ToString();
                row.Title.text = manager.DeckTrackTitle(index);
                row.Number.color = active ? AvTheme.RailInfo : AvTheme.Disabled;
                row.Title.color = active ? AvTheme.RailInfo : AvTheme.Unity(AvTokens.TextDim);
                row.Rule.color = active ? AvTheme.RailInfo : Color.clear;
            }
        }

        private static string FormatTime(float seconds)
        {
            int value = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (value / 60).ToString("00") + ":" + (value % 60).ToString("00");
        }
    }
}
