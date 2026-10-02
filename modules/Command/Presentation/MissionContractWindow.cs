using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>A client-local, read-only roster for the faction's secondary contracts.</summary>
    internal sealed class MissionContractWindow : MonoBehaviour
    {
        private const float Width = 860f;
        private const float Height = 780f;
        private const int Rows = 6;
        private const int SortOrder = 30002;

        private readonly List<SecondaryObjectiveView> roster = new List<SecondaryObjectiveView>(16);
        private AvWindow window;
        private AvSegmented filterControl;
        private AvSection rosterSection;
        private AvList list;
        private AvSection detailSection;
        private AvRow detailHeaderRow, detailTargetRow, detailRewardRow, detailStatusRow;
        private AvHazardBar detailProgressBar;
        private ContractBrief detailBrief;
        private int filter, selectedId = -1;
        private string lastBoardText = "";
        private float nextRefresh;
        private bool keyboardTouched, keyboardWas, pauseWas;
        private static int closedFrame = -10;
        private static MissionContractWindow openWindow;

        internal static bool IsOpen { get; private set; }
        internal static bool BlocksMap => IsOpen || Time.frameCount <= closedFrame + 1;

        internal static void CloseOpen() => openWindow?.Close();

        internal static MissionContractWindow Create()
        {
            var go = new GameObject("BoscaliMissionContractWindow", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var view = go.AddComponent<MissionContractWindow>();
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            view.Build();
            return view;
        }

        internal void Show()
        {
            if (openWindow != null && openWindow != this) openWindow.Close();
            openWindow = this;
            window.Show();
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
            Refresh();
        }

        internal void Close()
        {
            if (!IsOpen)
            {
                if (openWindow == this) openWindow = null;
                return;
            }
            IsOpen = false;
            if (openWindow == this) openWindow = null;
            closedFrame = Time.frameCount;
            window.Hide();
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
            Destroy(gameObject);
        }

        private void OnDestroy() => Close();

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .5f;
            Refresh();
        }

        private void Build()
        {
            window = AvWindow.Build(transform, "mission-desk", "SECONDARY / CONTRACT DESK", Width, Height, SortOrder);

            AvFlow body = window.Body;
            // Keep the fixed window's content viewport available to the flow's scroll layout.
            body.ViewportHeight = Height - 30f - AvGridTokens.Footer;
            rosterSection = body.Section(AvIcon.Bookmark, "CONTRACTS", "—");
            filterControl = body.Add(AvSegmented.Strip(body.Content,
                new[] { "ALL", "OFFERS", "ACTIVE", "CLOSED" }, () => filter, i => { filter = i; selectedId = -1; Refresh(); }));
            list = body.Add(new AvList(body.Content, window.Ticker, Rows, BindRow));
            list.RowClicked = SelectRow;

            detailSection = body.Section(AvIcon.User, "TASK", "—");
            detailHeaderRow = body.Add(new AvRow(body.Content));
            detailTargetRow = body.Add(new AvRow(body.Content));
            detailRewardRow = body.Add(new AvRow(body.Content));
            detailStatusRow = body.Add(new AvRow(body.Content));
            detailProgressBar = body.Add(new AvHazardBar(body.Content, "PROGRESS"));
            detailProgressBar.Help = "How far the selected contract has come, as the host reports it.";
            body.Section(AvIcon.ListDetails, "HOST BRIEF");
            detailBrief = body.Add(new ContractBrief(body.Content));

            AvControl closeButton = window.Root.GetComponentInChildren<AvControl>(true);
            if (closeButton != null) closeButton.Help = "Close the contract desk (Esc).";
            window.Footer.Set("Accept and cancel from the MIS bezel.");
        }

        private void BindRow(int index, AvRow row)
        {
            if (index < 0 || index >= roster.Count) { row.Set("—", "", "", AvState.Inert); return; }
            SecondaryObjectiveView entry = roster[index];
            AvState state = entry.Id == selectedId ? AvState.Info
                : entry.IsOffered ? AvState.Caution : entry.IsActive ? AvState.Ready : AvState.Inert;
            string owner = string.IsNullOrWhiteSpace(entry.AcceptedBy) ? "" : "  ·  " + entry.AcceptedBy;
            row.Set(MfdSecondaryObjectives.TitleLine(entry.Id, entry.Title),
                Phase(entry) + "  ·  " + MfdSecondaryObjectives.ChipLabel(entry) + owner, "", state);
            row.Help = "Inspect this faction contract.";
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= roster.Count) return;
            selectedId = roster[index].Id;
            Render(lastBoardText);
        }

        private void Refresh()
        {
            roster.Clear();
            string boardText;
            if (ModuleServices.TryGet(out ISecondaryObjectivesView view))
            {
                view.Refresh();
                IReadOnlyList<SecondaryObjectiveView> entries = view.Objectives;
                if (entries != null)
                    foreach (SecondaryObjectiveView entry in entries)
                    {
                        if (entry == null) continue;
                        if (filter == 1 && !entry.IsOffered) continue;
                        if (filter == 2 && !entry.IsActive) continue;
                        if (filter == 3 && (entry.IsOffered || entry.IsActive)) continue;
                        roster.Add(entry);
                    }
                boardText = string.IsNullOrWhiteSpace(view.Status) ? "WAITING FOR HOST REPORT" : view.Status;
            }
            else boardText = "MISSION DIRECTOR UNAVAILABLE";
            roster.Sort(CompareContracts);
            lastBoardText = boardText;
            Render(boardText);
        }

        /// <summary>The pure view step: renders the current <see cref="roster"/> without touching
        /// the host feed, so an offline check can seed the roster directly and render it.</summary>
        private void Render(string boardText = null)
        {
            int offers = 0, active = 0, closed = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].IsActive) active++;
                else if (roster[i].IsOffered) offers++;
                else closed++;
            }
            rosterSection.SetCaption((boardText ?? "") + "  ·  " + (filter == 0
                ? active + " ACTIVE / " + offers + (offers == 1 ? " OFFER / " : " OFFERS / ") + closed + " CLOSED"
                : roster.Count + " IN VIEW"));
            filterControl.Refresh();

            if (selectedId < 0 || !roster.Exists(entry => entry.Id == selectedId))
                selectedId = roster.Count > 0 ? roster[0].Id : -1;
            list.SetCount(roster.Count);

            SecondaryObjectiveView current = null;
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].Id == selectedId) { current = roster[i]; break; }
            ShowDetail(current);
        }

        private void ShowDetail(SecondaryObjectiveView entry)
        {
            bool has = entry != null;
            detailSection.SetCaption(has ? "FILE / " + entry.Id : "FILE / —");
            // Who took the contract rides on the header's second line; it used to be a row of its own.
            string taker = !has ? "" : !string.IsNullOrWhiteSpace(entry.AcceptedBy) ? entry.AcceptedBy
                : !entry.IsOffered ? "NOT REPORTED" : "AWAITING ACCEPTANCE";
            detailHeaderRow.Set(has ? MfdSecondaryObjectives.TitleLine(entry.Id, entry.Title) : "NO CONTRACT SELECTED",
                has ? Phase(entry) + "  ·  " + taker : "",
                has ? MfdSecondaryObjectives.ChipLabel(entry) : "—",
                has ? (entry.IsOffered ? AvState.Caution : entry.IsActive ? AvState.Ready : AvState.Info) : AvState.Inert);
            detailTargetRow.Set("TARGET", null,
                has && !string.IsNullOrWhiteSpace(entry.Target)
                    ? MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(entry.Target)) : "—", AvState.Info);
            detailRewardRow.Set("REWARD", null, has ? MfdSecondaryObjectives.PayoutLabel(entry) : "—", AvState.Ready);
            detailStatusRow.Set("HOST STATUS", null,
                has && !string.IsNullOrWhiteSpace(entry.Status) ? MfdSecondaryObjectives.PlainObjective(entry.Status) : "—", AvState.Info);
            float progress = has && !float.IsNaN(entry.Progress) && !float.IsInfinity(entry.Progress)
                ? Mathf.Clamp01(entry.Progress) : 0f;
            detailProgressBar.Set(progress, has ? TheaterReadout.Percent(progress) : "—",
                !has ? AvState.Inert : entry.IsActive ? AvState.Ready : AvState.Info);
            string description = has && !string.IsNullOrWhiteSpace(entry.Description)
                ? MfdSecondaryObjectives.PlainObjective(entry.Description)
                : has ? "No additional briefing supplied by the host." : "Select a contract to review its host briefing.";
            detailBrief.Set(description);
            detailHeaderRow.Help = description;
        }

        private sealed class ContractBrief : AvPart
        {
            private readonly TMP_Text text;

            public ContractBrief(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "HostBrief");
                text = AvText.Make(Rect, "Text", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public void Set(string value)
            {
                if (text.text == value) return;
                text.text = value;
                Changed();
            }

            public override float Measure(float width) => AvText.Height(text, width);

            public override void Place(AvSlot slot)
            {
                base.Place(slot);
                AvLay.Place(text.rectTransform, 0f, 0f, slot.W, slot.H);
            }

            public override void Restyle() =>
                text.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
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

        private static string Phase(SecondaryObjectiveView entry) =>
            entry.IsActive ? "IN FIELD" : entry.IsOffered ? "AWAITING ACCEPTANCE" :
            entry.IsComplete ? "COMPLETE" : "CLOSED";
    }
}
