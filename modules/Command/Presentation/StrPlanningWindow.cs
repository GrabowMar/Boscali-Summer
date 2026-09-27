using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>A live theater map with staff proposals and the operation's actual state.</summary>
    internal sealed class StrPlanningWindow : MonoBehaviour
    {
        private const float Width = 1824f;
        private const float Height = 968f;
        private const int SortOrder = 30001;
        private const int MaxProposals = 3;
        private const int MaxFronts = 6;
        private readonly TMP_Text[] proposalNames = new TMP_Text[MaxProposals];
        private readonly TMP_Text[] proposalBriefs = new TMP_Text[MaxProposals];
        private readonly AvButton[] proposalButtons = new AvButton[MaxProposals];
        private readonly GameObject[] proposalCards = new GameObject[MaxProposals];
        private readonly TMP_Text[] frontRows = new TMP_Text[MaxFronts];
        private readonly Image[] frontMarkers = new Image[MaxFronts];
        private readonly Image[] proposalMarkers = new Image[MaxProposals];
        private Image activeMarker;
        private TMP_Text activeName, activePhase, activeSummary, activeForces, staffLine, mapStatus;
        private AvButton cancelButton;
        private readonly AvButton[] postureButtons = new AvButton[3];
        private Image terrainImage;
        private RawImage controlImage;
        private RoomFrontlineGraphic frontGraphic;
        private RectTransform mapRect;
        private ITheaterWarView war;
        private ComMapOverlay overlay;
        private Canvas canvas;
        private GameObject surface;
        private RectTransform planningFrame;
        private Vector2 fittedCanvasSize;
        private float nextRefresh;
        private bool keyboardTouched, keyboardWas, pauseWas;
        private static int closedFrame = -10;

        internal static bool IsOpen { get; private set; }
        internal static bool BlocksMap => IsOpen || Time.frameCount <= closedFrame + 1;

        internal static StrPlanningWindow Create(ITheaterWarView war, ComMapOverlay overlay)
        {
            var go = new GameObject("BoscaliStrategyWindow", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var view = go.AddComponent<StrPlanningWindow>();
            view.war = war;
            view.overlay = overlay;
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

        internal void Show(int selection = 0)
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
        }

        private void OnDestroy() => Close();

        private void Update()
        {
            if (!IsOpen) return;
            FitRoom();
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            war?.Refresh();
            Refresh();
        }

        private void FitRoom()
        {
            if (planningFrame == null) return;
            Rect canvasArea = ((RectTransform)transform).rect;
            float canvasW = canvasArea.width > 0f ? canvasArea.width : 1920f;
            float canvasH = canvasArea.height > 0f ? canvasArea.height : 1080f;
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
            planningFrame.localScale = new Vector3(scale, scale, 1f);
            planningFrame.anchoredPosition = new Vector2(x + shownW * .5f, -(y + shownH * .5f));
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            surface = new GameObject("StrategySurface", typeof(RectTransform));
            var full = (RectTransform)surface.transform;
            full.SetParent(root, false);
            AvKit.Stretch(full);
            Image shade = AvRoomFrame.CreateBackdrop(full, .72f);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            CanvasGroup group;
            var frame = AvRoomFrame.CreateFrame(full, "PlanningFrame", out group);
            planningFrame = frame;
            group.alpha = 1f;
            frame.sizeDelta = new Vector2(Width, Height);
            FitRoom();
            AvKit.Panel(frame, new Rect(0f, 0f, Width, Height), AvTheme.Ground, AvSprites.Panel)
                .raycastTarget = true;
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, Height), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(Width - 1f, 0f, 1f, Height), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, -Height + 1f, Width, 1f), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, Width, 1f), AvTheme.Frame);

            var notch = new GameObject("StrategyNotch", typeof(RectTransform));
            var notchRect = (RectTransform)notch.transform;
            notchRect.SetParent(frame, false);
            AvKit.Place(notchRect, new Rect(24f, 32f, 164f, AvRoomFrame.NotchHeight));
            AvRoomFrame.NotchChrome chrome = AvRoomFrame.CreateNotchChrome(notchRect, "STRATEGY", "");
            AvRoomFrame.LayoutNotch(chrome, 164f);
            chrome.Fill.color = AvTheme.Surface;
            chrome.ActiveBar.color = AvTheme.RailReady;
            chrome.Label.color = AvTheme.TextPrimary;
            AvButton close = AvKit.HitButton(frame,
                new Rect(Width - 156f, 32f, 132f, AvRoomFrame.NotchHeight), Close);
            AvRoomFrame.NotchChrome closeChrome = AvRoomFrame.CreateNotchChrome(
                (RectTransform)close.transform, "× CLOSE", "ESC");
            AvRoomFrame.LayoutNotch(closeChrome, 132f);
            close.WithTooltip("Close operations room (Esc).");

            Label(frame, "THEATER / OPERATIONS ROOM", 28f, -20f, 800f, 30f, 23f,
                AvTheme.TextPrimary, true);
            Label(frame, "LIVE WAR PICTURE  /  CHOOSE INTENT, THEN FLY", 30f, -54f,
                970f, 18f, 12f, AvTheme.Dim);
            AvKit.Rule(frame, new Rect(28f, -82f, Width - 56f, 2f), AvTheme.RailInfo);

            BuildProposalColumn(frame);
            BuildMap(frame);
            BuildReportColumn(frame);
            AvKit.Rule(frame, new Rect(28f, -929f, Width - 56f, 1f), AvTheme.Hairline);
            staffLine = Label(frame, "STAFF LOG / AWAITING REPORT", 30f, -939f,
                Width - 60f, 18f, 12f, AvTheme.Dim);
        }

        private void BuildProposalColumn(RectTransform frame)
        {
            const float x = 24f, w = 342f;
            AvKit.Panel(frame, new Rect(x, -100f, w, 812f), AvTheme.SurfaceInert);
            Label(frame, "STAFF PROPOSALS", x + 18f, -116f, w - 36f, 25f, 17f,
                AvTheme.TextPrimary, true);
            Label(frame, "An opening creates a short choice.", x + 18f, -144f,
                w - 36f, 35f, 12f, AvTheme.Dim);
            for (int i = 0; i < MaxProposals; i++)
            {
                int slot = i;
                float y = -196f - i * 154f;
                proposalCards[i] = new GameObject("StaffProposal" + i, typeof(RectTransform));
                var card = (RectTransform)proposalCards[i].transform;
                card.SetParent(frame, false);
                AvKit.Place(card, new Rect(x + 18f, y, w - 36f, 142f));
                AvKit.Panel(card, new Rect(0f, 0f, w - 36f, 142f), AvTheme.Surface);
                AvKit.Outline(card, new Rect(0f, 0f, w - 36f, 142f), AvTheme.Hairline);
                AvKit.Rule(card, new Rect(0f, 0f, 3f, 142f), AvTheme.RailReady);
                proposalNames[i] = Label(card, "—", 13f, -12f, w - 62f, 22f, 14f,
                    AvTheme.TextPrimary, true);
                proposalBriefs[i] = Label(card, "", 13f, -43f, w - 62f, 72f, 12f, AvTheme.Dim);
                proposalBriefs[i].enableWordWrapping = true;
                proposalButtons[i] = AvKit.Button(card, "CHOOSE THIS PLAN",
                    new Rect(13f, -109f, w - 62f, 26f), () => Pick(slot), 12f,
                    AvButtonStyle.Primary);
            }
            Label(frame, "STAFF POSTURE", x + 18f, -674f, w - 36f, 19f, 14f,
                AvTheme.TextPrimary, true);
            string[] names = { "CAUTIOUS", "STEADY", "BOLD" };
            for (int i = 0; i < names.Length; i++)
            {
                TheaterWarPosture posture = (TheaterWarPosture)i;
                postureButtons[i] = AvKit.Button(frame, names[i],
                    new Rect(x + 18f + i * 103f, -704f, 98f, 28f),
                    () => { if (war != null && war.RequestPosture(posture)) Refresh(); },
                    11f);
            }
            Label(frame, "Staff spends from the faction pool.", x + 18f, -752f,
                w - 36f, 30f, 12f, AvTheme.Dim);
            Label(frame, "No unit orders are issued here.", x + 18f, -786f,
                w - 36f, 28f, 12f, AvTheme.Dim);
        }

        private void BuildMap(RectTransform frame)
        {
            const float x = 384f, y = -100f, w = 1002f, h = 812f;
            AvKit.Panel(frame, new Rect(x, y, w, h), AvTheme.SurfaceInert);
            Label(frame, "LIVE THEATER MAP", x + 20f, y - 15f, 460f, 23f, 17f,
                AvTheme.TextPrimary, true);
            Label(frame, "FRONT / CONTROL / STAFF INTENT", x + 520f, y - 18f,
                460f, 18f, 12f, AvTheme.Dim);
            mapRect = new GameObject("TheaterMap", typeof(RectTransform))
                .GetComponent<RectTransform>();
            mapRect.SetParent(frame, false);
            AvKit.Place(mapRect, new Rect(x + 20f, y - 52f, w - 40f, 702f));
            AvKit.Panel(mapRect, new Rect(0f, 0f, mapRect.sizeDelta.x, mapRect.sizeDelta.y),
                AvTheme.Ground);
            terrainImage = AvKit.Panel(mapRect,
                new Rect(0f, 0f, 962f, 702f), Color.white);
            terrainImage.raycastTarget = false;
            var controlObj = new GameObject("ControlField", typeof(RectTransform), typeof(RawImage));
            controlImage = controlObj.GetComponent<RawImage>();
            controlObj.transform.SetParent(mapRect, false);
            AvKit.Place((RectTransform)controlObj.transform, new Rect(0f, 0f, 962f, 702f));
            controlImage.raycastTarget = false;
            controlImage.color = Color.white;
            var frontObj = new GameObject("LiveFront", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(RoomFrontlineGraphic));
            frontGraphic = frontObj.GetComponent<RoomFrontlineGraphic>();
            frontObj.transform.SetParent(mapRect, false);
            AvKit.Place((RectTransform)frontObj.transform, new Rect(0f, 0f, 962f, 702f));
            frontGraphic.raycastTarget = false;
            for (int i = 0; i < MaxFronts; i++)
                frontMarkers[i] = Marker(mapRect, AvTheme.RailCaution);
            for (int i = 0; i < MaxProposals; i++)
                proposalMarkers[i] = Marker(mapRect, AvTheme.RailReady);
            activeMarker = Marker(mapRect, AvTheme.RailInfo);
            AvKit.Outline(mapRect, new Rect(0f, 0f, 962f, 702f), AvTheme.Hairline);
            mapStatus = Label(frame, "MAP DATA UNAVAILABLE", x + 20f, y - 766f,
                w - 40f, 22f, 12f, AvTheme.Dim);
        }

        private void BuildReportColumn(RectTransform frame)
        {
            const float x = 1402f, w = 398f;
            AvKit.Panel(frame, new Rect(x, -100f, w, 812f), AvTheme.SurfaceInert);
            Label(frame, "PRIMARY OPERATION", x + 20f, -118f, w - 40f, 23f,
                16f, AvTheme.TextPrimary, true);
            activeName = Label(frame, "STAFF ASSESSING", x + 20f, -158f,
                w - 40f, 56f, 20f, AvTheme.TextPrimary, true);
            activeName.enableWordWrapping = true;
            activePhase = Label(frame, "", x + 20f, -230f, w - 40f, 20f,
                14f, AvTheme.RailInfo, true);
            activeSummary = Label(frame, "", x + 20f, -265f, w - 40f, 92f,
                13f, AvTheme.Dim);
            activeSummary.enableWordWrapping = true;
            activeForces = Label(frame, "", x + 20f, -364f, w - 40f, 22f,
                12f, AvTheme.TextPrimary);
            cancelButton = AvKit.Button(frame, "CALL OFF / REPLAN",
                new Rect(x + 20f, -402f, w - 40f, 34f), CancelOperation,
                13f, AvButtonStyle.Danger);
            cancelButton.WithTooltip("Call off this operation and request fresh staff choices.");
            AvKit.Rule(frame, new Rect(x + 20f, -459f, w - 40f, 1f), AvTheme.Hairline);
            Label(frame, "FRONTS / FIELD REPORTS", x + 20f, -476f, w - 40f, 22f,
                15f, AvTheme.TextPrimary, true);
            for (int i = 0; i < MaxFronts; i++)
                frontRows[i] = Label(frame, "", x + 20f, -513f - i * 54f,
                    w - 40f, 46f, 12f, AvTheme.Dim);
        }

        private void Refresh()
        {
            bool ready = war != null && war.Available;
            IReadOnlyList<TheaterProposalView> proposals = ready ? war.Proposals : null;
            int proposalCount = proposals != null ? Mathf.Min(proposals.Count, MaxProposals) : 0;
            for (int i = 0; i < MaxProposals; i++)
            {
                bool visible = i < proposalCount;
                proposalCards[i].SetActive(visible);
                if (!visible) continue;
                TheaterProposalView proposal = proposals[i];
                proposalNames[i].text = proposal.Kind + " / " + proposal.Label;
                proposalBriefs[i].text = proposal.Brief + "\n" + proposal.Forces +
                    "  ·  RISK " + proposal.Risk + "  ·  " +
                    Mathf.CeilToInt(Mathf.Max(0f, proposal.SecondsRemaining)) + "S";
                proposalButtons[i].SetEnabled(war.CanCommand);
            }
            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;
            activeName.text = active == null ? "NO PRIMARY OPERATION" : active.Label;
            activePhase.text = active == null ? "LOCAL FIGHTS CONTINUE" : active.Kind + " / " + active.Phase;
            activeSummary.text = active == null
                ? "The staff is monitoring several fronts for an opening." : active.Summary;
            activeForces.text = active == null ? "" :
                active.GroundGroups + " GROUND  /  " + active.AirGroups + " AIR  /  " +
                active.NavalGroups + " NAVAL GROUPS";
            cancelButton.SetEnabled(ready && war.CanCommand && active != null);
            for (int i = 0; i < postureButtons.Length; i++)
            {
                postureButtons[i].SetEnabled(ready && war.CanCommand);
                postureButtons[i].SetLatched(ready && (int)war.Posture == i);
            }
            IReadOnlyList<TheaterFrontView> fronts = ready ? war.Fronts : null;
            int frontCount = fronts != null ? Mathf.Min(fronts.Count, MaxFronts) : 0;
            for (int i = 0; i < MaxFronts; i++)
            {
                TheaterFrontView front = i < frontCount ? fronts[i] : null;
                frontRows[i].text = front == null ? "" : front.Observed
                    ? front.Label + "\n" + front.Status + " · " +
                      Mathf.RoundToInt(Mathf.Clamp01(front.Pressure) * 100f) + "% PRESSURE"
                    : front.Label + "\nRUMOR / UNCONFIRMED";
            }
            IReadOnlyList<string> log = ready ? war.StaffLog : null;
            staffLine.text = log != null && log.Count > 0 ? "STAFF LOG  ·  " + log[0]
                : "STAFF LOG  ·  NO RECENT REPORT";
            RefreshMap(active, proposals, fronts);
        }

        private void RefreshMap(TheaterLiveOperationView active,
            IReadOnlyList<TheaterProposalView> proposals, IReadOnlyList<TheaterFrontView> fronts)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Image source = map?.mapImage?.GetComponent<Image>();
            terrainImage.sprite = source != null ? source.sprite : null;
            terrainImage.enabled = terrainImage.sprite != null;
            Transform control = map?.mapImage?.transform.Find("ComSectorGridOverlay");
            RawImage sourceControl = control != null ? control.GetComponent<RawImage>() : null;
            controlImage.texture = sourceControl != null ? sourceControl.texture : null;
            controlImage.enabled = controlImage.texture != null;
            var grid = overlay?.Grid;
            frontGraphic.SetSource(grid);
            mapStatus.text = terrainImage.enabled
                ? "LIVE TERRAIN  ·  WHITE TRACE: FRONT  ·  GREEN: OFFER  ·  CYAN: ACTIVE  ·  AMBER: CONTACT"
                : "NATIVE MAP IMAGE UNAVAILABLE";
            for (int i = 0; i < MaxFronts; i++)
            {
                TheaterFrontView front = fronts != null && i < fronts.Count ? fronts[i] : null;
                PlaceMarker(frontMarkers[i], front != null && front.Observed, front?.X ?? float.NaN,
                    front?.Z ?? float.NaN, grid);
            }
            for (int i = 0; i < MaxProposals; i++)
            {
                TheaterProposalView proposal = proposals != null && i < proposals.Count ? proposals[i] : null;
                PlaceMarker(proposalMarkers[i], proposal != null, proposal?.X ?? float.NaN,
                    proposal?.Z ?? float.NaN, grid);
            }
            PlaceMarker(activeMarker, active != null, active?.X ?? float.NaN,
                active?.Z ?? float.NaN, grid);
        }

        private static Image Marker(RectTransform parent, Color color)
        {
            Image marker = AvKit.Panel(parent, new Rect(0f, 0f, 12f, 12f),
                color, AvSprites.White);
            marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            marker.raycastTarget = false;
            marker.enabled = false;
            return marker;
        }

        private static void PlaceMarker(Image marker, bool known, float x, float z,
            BoscaliSummer.Features.Command.Runtime.TacticalSectorGrid grid)
        {
            if (!known || grid == null || !(grid.WorldSizeX > 0f) || !(grid.WorldSizeY > 0f) ||
                float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z))
            {
                marker.enabled = false;
                return;
            }
            float u = x / grid.WorldSizeX + .5f;
            float v = z / grid.WorldSizeY + .5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                marker.enabled = false;
                return;
            }
            AvKit.Place(marker.rectTransform,
                new Rect(u * 962f - 6f, -(1f - v) * 702f + 6f, 12f, 12f));
            marker.enabled = true;
        }

        private void Pick(int slot)
        {
            IReadOnlyList<TheaterProposalView> proposals = war?.Proposals;
            if (proposals == null || slot < 0 || slot >= proposals.Count) return;
            TheaterProposalView proposal = proposals[slot];
            if (war.RequestPick(proposal.Id, proposal.Revision)) Refresh();
        }

        private void CancelOperation()
        {
            TheaterLiveOperationView active = war?.ActiveOperation;
            if (active != null && war.RequestCancel(active.Id, active.Revision)) Refresh();
        }

        private void ViewMap() => Close();

        private static TMP_Text Label(RectTransform parent, string value,
            float x, float y, float width, float height, float size, Color color, bool bold = false)
        {
            TMP_Text label = AvKit.Label(parent, value, new Rect(x, y, width, height), color,
                size, bold ? FontStyles.Bold : FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        /// <summary>World-space front traces projected over the room's flat native map sprite.</summary>
        private sealed class RoomFrontlineGraphic : MaskableGraphic
        {
            private const int SegmentBudget = 1024;
            private readonly FrontlineTracePoint[] points =
                new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
            private readonly int[] lengths = new int[FrontlineTraceLimits.MaximumTraces];
            private readonly float[] pressure = new float[FrontlineTraceLimits.MaximumTraces];
            private BoscaliSummer.Features.Command.Runtime.TacticalSectorGrid source;
            private ulong drawnHash;

            public void SetSource(BoscaliSummer.Features.Command.Runtime.TacticalSectorGrid grid)
            {
                ulong hash = grid != null ? grid.FrontlineHash : 0UL;
                if (ReferenceEquals(grid, source) && hash == drawnHash) return;
                source = grid;
                drawnHash = hash;
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper mesh)
            {
                mesh.Clear();
                if (source == null || !(source.WorldSizeX > 0f) || !(source.WorldSizeY > 0f)) return;
                int traces = source.CopyFrontlineTraces(points, lengths, pressure);
                if (traces <= 0) return;
                Rect rect = rectTransform.rect;
                int total = 0;
                for (int t = 0; t < traces; t++) total += lengths[t];
                int stride = Mathf.Max(1, Mathf.CeilToInt((float)total / SegmentBudget));
                int start = 0;
                for (int t = 0; t < traces; t++)
                {
                    int end = Mathf.Min(points.Length, start + lengths[t]);
                    if (end - start < 2) { start = end; continue; }
                    Vector2 previous = Project(points[start], rect);
                    for (int p = start + stride; p < end; p += stride)
                    {
                        Vector2 next = Project(points[p], rect);
                        DrawLine(mesh, previous, next);
                        previous = next;
                    }
                    DrawLine(mesh, previous, Project(points[end - 1], rect));
                    start = end;
                }
            }

            private Vector2 Project(FrontlineTracePoint point, Rect rect) =>
                new Vector2(rect.xMin + (point.X / source.WorldSizeX + .5f) * rect.width,
                    rect.yMin + (point.Z / source.WorldSizeY + .5f) * rect.height);

            private static void DrawLine(VertexHelper mesh, Vector2 a, Vector2 b)
            {
                Vector2 delta = b - a;
                if (delta.sqrMagnitude < .01f) return;
                Vector2 normal = new Vector2(-delta.y, delta.x).normalized * 1.5f;
                int start = mesh.currentVertCount;
                Color32 ink = new Color32(235, 245, 252, 230);
                Add(mesh, a - normal, ink);
                Add(mesh, a + normal, ink);
                Add(mesh, b + normal, ink);
                Add(mesh, b - normal, ink);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }

            private static void Add(VertexHelper mesh, Vector2 point, Color32 ink)
            {
                UIVertex vertex = UIVertex.simpleVert;
                vertex.position = point;
                vertex.color = ink;
                mesh.AddVert(vertex);
            }
        }
    }
}
