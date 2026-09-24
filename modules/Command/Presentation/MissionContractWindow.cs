using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>A client-local, read-only roster for the faction's secondary contracts.</summary>
    internal sealed class MissionContractWindow : MonoBehaviour
    {
        private const float Width = 1740f;
        private const float Height = 920f;
        private const int Rows = 7;
        private const int SortOrder = 30002;
        private readonly List<SecondaryObjectiveView> roster = new List<SecondaryObjectiveView>(16);
        private readonly Image[] rowBackgrounds = new Image[Rows];
        private readonly Image[] rowRails = new Image[Rows];
        private readonly TMP_Text[] rowTitles = new TMP_Text[Rows];
        private readonly TMP_Text[] rowDetails = new TMP_Text[Rows];
        private readonly AvButton[] rowHits = new AvButton[Rows];
        private readonly AvButton[] filters = new AvButton[4];
        private Canvas canvas;
        private GameObject surface;
        private RectTransform frame;
        private TMP_Text boardState, counts, pageLabel, detailTitle, detailPhase,
            detailTimer, detailDescription, detailTarget, detailAccepted, detailReward,
            detailStatus, detailId, detailProgress;
        private Image progressFill, detailRail;
        private AvButton previous, next;
        private Vector2 fittedCanvasSize;
        private int filter, page, selectedId = -1;
        private float nextRefresh;
        private bool keyboardTouched, keyboardWas, pauseWas;
        private static int closedFrame = -10;

        internal static bool IsOpen { get; private set; }
        internal static bool BlocksMap => IsOpen || Time.frameCount <= closedFrame + 1;

        internal static MissionContractWindow Create()
        {
            var go = new GameObject("BoscaliMissionContractWindow", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var view = go.AddComponent<MissionContractWindow>();
            view.canvas = go.GetComponent<Canvas>();
            view.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            view.canvas.sortingOrder = SortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            view.Build();
            view.canvas.enabled = false;
            view.surface.SetActive(false);
            return view;
        }

        internal void Show()
        {
            FitRoom();
            canvas.enabled = true;
            surface.SetActive(true);
            if (!IsOpen)
            {
                pauseWas = GameplayUI.AllowPauseKeybind;
                GameplayUI.AllowPauseKeybind = false;
                keyboardTouched = Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                                  Rewired.ReInput.controllers.Keyboard != null;
                if (keyboardTouched)
                {
                    keyboardWas = Rewired.ReInput.controllers.Keyboard.enabled;
                    Rewired.ReInput.controllers.Keyboard.enabled = false;
                }
            }
            IsOpen = true;
            AvButton.ClearTooltip();
            Refresh();
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closedFrame = Time.frameCount;
            canvas.enabled = false;
            surface.SetActive(false);
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
            AvButton.ClearTooltip();
            Destroy(gameObject);
        }

        private void OnDestroy() => Close();

        private void Update()
        {
            if (!IsOpen) return;
            FitRoom();
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .5f;
            Refresh();
        }

        private void FitRoom()
        {
            if (frame == null) return;
            Rect area = ((RectTransform)transform).rect;
            float canvasW = area.width > 0f ? area.width : 1920f;
            float canvasH = area.height > 0f ? area.height : 1080f;
            if (Mathf.Abs(fittedCanvasSize.x - canvasW) < .5f &&
                Mathf.Abs(fittedCanvasSize.y - canvasH) < .5f) return;
            fittedCanvasSize = new Vector2(canvasW, canvasH);
            Rect fit = AvRoomFrame.WindowRect(canvasW, canvasH);
            float scale = Mathf.Min(1f, Mathf.Min(fit.width / Width, fit.height / Height));
            float shownW = Width * scale, shownH = Height * scale;
            float x = fit.x + (fit.width - shownW) * .5f;
            float y = Mathf.Max(fit.y, AvRoomFrame.NotchHeight * scale + 8f);
            if (y + shownH > canvasH - 8f)
                y = Mathf.Max(AvRoomFrame.NotchHeight * scale, canvasH - shownH - 8f);
            frame.localScale = new Vector3(scale, scale, 1f);
            frame.anchoredPosition = new Vector2(x + shownW * .5f, -(y + shownH * .5f));
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            surface = new GameObject("MissionContractSurface", typeof(RectTransform));
            var full = (RectTransform)surface.transform;
            full.SetParent(root, false);
            AvKit.Stretch(full);
            Image backdrop = AvRoomFrame.CreateBackdrop(full, .74f);
            backdrop.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            CanvasGroup group;
            frame = AvRoomFrame.CreateFrame(full, "ContractDeskFrame", out group);
            group.alpha = 1f;
            frame.sizeDelta = new Vector2(Width, Height);
            FitRoom();
            AvKit.Panel(frame, new Rect(0f, 0f, Width, Height), AvTheme.Ground, AvSprites.Panel)
                .raycastTarget = true;
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, Width, 1f), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, -Height + 1f, Width, 1f), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, Height), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(Width - 1f, 0f, 1f, Height), AvTheme.Frame);

            var notchObject = new GameObject("MissionDeskNotch", typeof(RectTransform));
            var notch = (RectTransform)notchObject.transform;
            notch.SetParent(frame, false);
            AvKit.Place(notch, new Rect(24f, 32f, 170f, AvRoomFrame.NotchHeight));
            AvRoomFrame.NotchChrome chrome = AvRoomFrame.CreateNotchChrome(notch, "MISSIONS", "");
            AvRoomFrame.LayoutNotch(chrome, 170f);
            chrome.Fill.color = AvTheme.Surface;
            chrome.ActiveBar.color = AvTheme.RailReady;
            AvButton close = AvKit.HitButton(frame,
                new Rect(Width - 156f, 32f, 132f, AvRoomFrame.NotchHeight), Close);
            close.WithTooltip("Close the contract desk (Esc).");
            AvRoomFrame.NotchChrome closeChrome = AvRoomFrame.CreateNotchChrome(
                (RectTransform)close.transform, "× CLOSE", "ESC");
            AvRoomFrame.LayoutNotch(closeChrome, 132f);

            Label(frame, "SECONDARY / CONTRACT DESK", 28f, -20f, 900f, 30f, 23f,
                AvTheme.TextPrimary, true);
            Label(frame, "FACTION-WIDE TASKING  /  HOST REPORT", 30f, -53f, 1000f,
                18f, 12f, AvTheme.Dim);
            Label(frame, "READ ONLY", 1418f, -28f, 270f, 25f, 15f, AvTheme.RailInfo, true);
            AvKit.Rule(frame, new Rect(28f, -82f, Width - 56f, 2f), AvTheme.RailInfo);

            AvKit.Panel(frame, new Rect(24f, -101f, 592f, 767f), AvTheme.SurfaceInert);
            AvKit.Panel(frame, new Rect(632f, -101f, 1084f, 767f), AvTheme.SurfaceInert);
            Label(frame, "CONTRACT ROSTER", 42f, -116f, 285f, 24f, 16f,
                AvTheme.TextPrimary, true);
            counts = Label(frame, "—", 332f, -116f, 264f, 24f, 12f, AvTheme.Dim);
            boardState = Label(frame, "AWAITING HOST", 42f, -145f, 550f, 21f, 12f,
                AvTheme.RailInfo);
            boardState.richText = false;

            string[] names = { "ALL", "OFFERS", "ACTIVE", "CLOSED" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                filters[i] = AvKit.Tab(frame, names[i],
                    new Rect(42f + 138f * i, -177f, 130f, 34f),
                    () => { filter = index; page = 0; selectedId = -1; Refresh(); });
            }
            for (int i = 0; i < Rows; i++)
            {
                int index = i;
                float y = -225f - 78f * i;
                rowBackgrounds[i] = AvKit.Panel(frame, new Rect(42f, y, 554f, 70f), AvTheme.Surface);
                rowRails[i] = AvKit.Rule(frame, new Rect(42f, y, 3f, 70f), AvTheme.RailInert);
                rowTitles[i] = Label(frame, "—", 56f, y - 8f, 520f, 22f, 15f,
                    AvTheme.TextPrimary, true);
                rowDetails[i] = Label(frame, "—", 56f, y - 34f, 520f, 20f, 11f,
                    AvTheme.Dim);
                rowTitles[i].richText = false;
                rowDetails[i].richText = false;
                rowHits[i] = AvKit.HitButton(frame, new Rect(42f, y, 554f, 70f),
                    () => SelectRow(index));
                rowHits[i].WithTooltip("Inspect this faction contract.");
            }
            previous = AvKit.Button(frame, "‹", new Rect(42f, -786f, 50f, 38f),
                () => ChangePage(-1));
            pageLabel = Label(frame, "PAGE 1 / 1", 104f, -790f, 424f, 30f, 12f,
                AvTheme.Dim);
            next = AvKit.Button(frame, "›", new Rect(546f, -786f, 50f, 38f),
                () => ChangePage(1));

            Label(frame, "TASK FILE", 654f, -116f, 400f, 25f, 16f,
                AvTheme.TextPrimary, true);
            detailId = Label(frame, "—", 1460f, -116f, 220f, 25f, 13f,
                AvTheme.Dim);
            AvKit.Rule(frame, new Rect(654f, -153f, 1038f, 1f), AvTheme.Hairline);
            detailRail = AvKit.Rule(frame, new Rect(654f, -177f, 4f, 99f), AvTheme.RailInert);
            detailTitle = Label(frame, "SELECT A CONTRACT", 675f, -177f, 995f, 42f, 25f,
                AvTheme.TextPrimary, true);
            detailTitle.richText = false;
            detailPhase = Label(frame, "—", 675f, -222f, 650f, 24f, 14f,
                AvTheme.RailInfo, true);
            detailTimer = Label(frame, "—", 1390f, -222f, 280f, 24f, 14f,
                AvTheme.Dim);
            AvKit.Panel(frame, new Rect(654f, -303f, 1038f, 10f), AvTheme.Ground);
            progressFill = AvKit.Panel(frame, new Rect(654f, -303f, 0f, 10f), AvTheme.RailReady);
            detailProgress = Label(frame, "—", 654f, -327f, 1038f, 20f, 12f,
                AvTheme.Dim);
            AvKit.Rule(frame, new Rect(654f, -359f, 1038f, 1f), AvTheme.Hairline);
            Label(frame, "MISSION ORDER", 654f, -379f, 450f, 24f, 12f,
                AvTheme.RailInfo, true);
            detailDescription = Label(frame, "Select a row to review the host report.",
                654f, -413f, 1038f, 101f, 17f, AvTheme.TextPrimary);
            detailDescription.enableWordWrapping = true;
            detailDescription.overflowMode = TextOverflowModes.Truncate;
            detailDescription.richText = false;

            Metric(frame, "TARGET", 654f, -546f, out detailTarget);
            Metric(frame, "ACCEPTED BY", 1183f, -546f, out detailAccepted);
            Metric(frame, "REWARD", 654f, -641f, out detailReward);
            Metric(frame, "HOST STATUS", 1183f, -641f, out detailStatus);
            detailTarget.richText = false;
            detailAccepted.richText = false;
            detailStatus.richText = false;
            AvKit.Panel(frame, new Rect(654f, -755f, 1038f, 92f), AvTheme.Surface);
            AvKit.Rule(frame, new Rect(654f, -755f, 3f, 92f), AvTheme.RailInfo);
            Label(frame, "FACTION-WIDE TASKING", 674f, -768f, 970f, 20f, 12f,
                AvTheme.RailInfo, true);
            Label(frame,
                "The named pilot accepted the contract for this faction. Any friendly pilot may help complete it.",
                674f, -797f, 980f, 40f, 13f, AvTheme.Dim);
            AvKit.Rule(frame, new Rect(28f, -883f, Width - 56f, 1f), AvTheme.Hairline);
            Label(frame, "MISSION DESK  /  FACTION-WIDE CONTRACTS  /  ACCEPT & CANCEL FROM THE MIS BEZEL",
                42f, -889f, Width - 84f, 22f, 11f, AvTheme.Dim);
        }

        private static TMP_Text Label(RectTransform parent, string value, float x, float y,
            float w, float h, float size, Color color, bool bold = false) =>
            AvKit.Label(parent, value, new Rect(x, y, w, h), color, size,
                bold ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

        private static void Metric(RectTransform parent, string name, float x, float y,
            out TMP_Text value)
        {
            AvKit.Panel(parent, new Rect(x, y, 509f, 80f), AvTheme.Surface);
            AvKit.Rule(parent, new Rect(x, y, 3f, 80f), AvTheme.RailInfo);
            Label(parent, name, x + 17f, y - 10f, 468f, 17f, 11f, AvTheme.Dim, true);
            value = Label(parent, "—", x + 17f, y - 34f, 468f, 31f, 18f,
                AvTheme.TextPrimary, true);
        }

        private void ChangePage(int delta)
        {
            page = Mathf.Clamp(page + delta, 0, Math.Max(0, (roster.Count - 1) / Rows));
            selectedId = -1;
            Refresh();
        }

        private void SelectRow(int index)
        {
            int at = page * Rows + index;
            if (at < 0 || at >= roster.Count) return;
            selectedId = roster[at].Id;
            Render();
        }

        private void Refresh()
        {
            roster.Clear();
            if (ModServices.TryGet(out ISecondaryObjectivesView view))
            {
                view.Refresh();
                IReadOnlyList<SecondaryObjectiveView> entries = view.Objectives;
                if (entries != null) foreach (SecondaryObjectiveView entry in entries)
                {
                    if (entry == null) continue;
                    if (filter == 1 && !entry.IsOffered) continue;
                    if (filter == 2 && !entry.IsActive) continue;
                    if (filter == 3 && (entry.IsOffered || entry.IsActive)) continue;
                    roster.Add(entry);
                }
                boardState.text = string.IsNullOrWhiteSpace(view.Status)
                    ? "WAITING FOR HOST REPORT" : view.Status;
            }
            else boardState.text = "MISSION DIRECTOR UNAVAILABLE";
            roster.Sort(CompareContracts);
            page = Mathf.Clamp(page, 0, Math.Max(0, (roster.Count - 1) / Rows));
            if (selectedId < 0 || !roster.Exists(entry => entry.Id == selectedId))
                selectedId = roster.Count > 0 ? roster[page * Rows].Id : -1;
            Render();
        }

        private static int CompareContracts(SecondaryObjectiveView a, SecondaryObjectiveView b)
        {
            int groupA = a.IsActive ? 0 : a.IsOffered ? 1 : 2;
            int groupB = b.IsActive ? 0 : b.IsOffered ? 1 : 2;
            if (groupA != groupB) return groupA.CompareTo(groupB);
            if (groupA == 2) return b.Id.CompareTo(a.Id);
            float timeA = float.IsNaN(a.SecondsRemaining) || float.IsInfinity(a.SecondsRemaining)
                ? float.MaxValue : a.SecondsRemaining;
            float timeB = float.IsNaN(b.SecondsRemaining) || float.IsInfinity(b.SecondsRemaining)
                ? float.MaxValue : b.SecondsRemaining;
            int time = timeA.CompareTo(timeB);
            return time != 0 ? time : a.Id.CompareTo(b.Id);
        }

        private void Render()
        {
            int offers = 0, active = 0, closed = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                SecondaryObjectiveView entry = roster[i];
                if (entry.IsActive) active++;
                else if (entry.IsOffered) offers++;
                else closed++;
            }
            counts.text = filter == 0
                ? active + " ACTIVE  /  " + offers + " OFFER  /  " + closed + " CLOSED"
                : roster.Count + " IN VIEW";
            for (int i = 0; i < filters.Length; i++) filters[i].SetLatched(filter == i);
            int pages = Math.Max(1, (roster.Count + Rows - 1) / Rows);
            pageLabel.text = "PAGE " + (page + 1) + " / " + pages +
                             "     •     " + roster.Count + " CONTRACTS";
            previous.SetEnabled(page > 0);
            next.SetEnabled(page + 1 < pages);
            for (int i = 0; i < Rows; i++)
            {
                int at = page * Rows + i;
                bool show = at < roster.Count;
                rowHits[i].gameObject.SetActive(show);
                rowBackgrounds[i].gameObject.SetActive(show);
                rowTitles[i].gameObject.SetActive(show);
                rowDetails[i].gameObject.SetActive(show);
                rowRails[i].gameObject.SetActive(show);
                if (!show) continue;
                SecondaryObjectiveView entry = roster[at];
                bool selected = entry.Id == selectedId;
                rowRails[i].color = selected ? AvTheme.RailReady :
                    entry.IsOffered ? AvTheme.RailCaution :
                    entry.IsActive ? AvTheme.RailInfo : AvTheme.RailInert;
                rowTitles[i].text = MfdSecondaryObjectives.TitleLine(entry.Id, entry.Title);
                rowTitles[i].color = selected ? AvTheme.RailReady : AvTheme.TextPrimary;
                string owner = string.IsNullOrWhiteSpace(entry.AcceptedBy) ? "" :
                    "  •  ACCEPTED BY " + entry.AcceptedBy;
                rowDetails[i].text = Phase(entry) + "  •  " + MfdSecondaryObjectives.ChipLabel(entry) + owner;
            }
            SecondaryObjectiveView current = null;
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].Id == selectedId) { current = roster[i]; break; }
            ShowDetail(current);
        }

        private void ShowDetail(SecondaryObjectiveView entry)
        {
            bool has = entry != null;
            detailId.text = has ? "FILE / " + entry.Id : "FILE / —";
            detailTitle.text = has ? MfdSecondaryObjectives.TitleLine(entry.Id, entry.Title) :
                "NO CONTRACT SELECTED";
            detailPhase.text = has ? Phase(entry) : "NO HOST TASK FILE";
            detailPhase.color = has && entry.IsOffered ? AvTheme.RailCaution :
                has && entry.IsActive ? AvTheme.RailReady : AvTheme.RailInfo;
            detailRail.color = detailPhase.color;
            detailTimer.text = has ? MfdSecondaryObjectives.ChipLabel(entry) : "—";
            detailDescription.text = has && !string.IsNullOrWhiteSpace(entry.Description)
                ? MfdSecondaryObjectives.PlainObjective(entry.Description)
                : "No current contract report is available.";
            detailTarget.text = has && !string.IsNullOrWhiteSpace(entry.Target) ?
                MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(entry.Target)) : "—";
            detailAccepted.text = !has ? "—" : !string.IsNullOrWhiteSpace(entry.AcceptedBy)
                ? entry.AcceptedBy : !entry.IsOffered ? "NOT REPORTED" : "AWAITING ACCEPTANCE";
            detailReward.text = has ? MfdSecondaryObjectives.PayoutLabel(entry) : "—";
            detailStatus.text = has && !string.IsNullOrWhiteSpace(entry.Status) ?
                MfdSecondaryObjectives.PlainObjective(entry.Status) : "—";
            float progress = has && !float.IsNaN(entry.Progress) && !float.IsInfinity(entry.Progress)
                ? Mathf.Clamp01(entry.Progress) : 0f;
            progressFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                1038f * progress);
            detailProgress.text = has ? "TASK PROGRESS  /  " + Mathf.RoundToInt(progress * 100f) + "%" : "TASK PROGRESS  /  —";
        }

        private static string Phase(SecondaryObjectiveView entry) =>
            entry.IsActive ? "IN FIELD" : entry.IsOffered ? "AWAITING ACCEPTANCE" :
            entry.IsComplete ? "COMPLETE / PAID" : "CLOSED";
    }
}
