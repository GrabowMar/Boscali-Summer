using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
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
        private const float Width = 900f;
        private const float Height = 860f;
        private const int SortOrder = 30001;
        private const int MaxProposals = 3;
        private const int MaxFronts = 6;

        private AvWindow window;
        private AvRow operationRow;
        private AvRowStack proposals;
        private AvList fronts;
        private AvSegmented postureControl;
        private TheaterMapPart map;
        private ITheaterWarView war;
        private ComMapOverlay overlay;
        private TheaterWarPosture selectedPosture;
        private float nextRefresh;
        private bool keyboardTouched, keyboardWas, pauseWas;
        private static int closedFrame = -10;

        internal static bool IsOpen { get; private set; }
        internal static bool BlocksMap => IsOpen || Time.frameCount <= closedFrame + 1;

        internal static StrPlanningWindow Create(ITheaterWarView war, ComMapOverlay overlay)
        {
            var go = new GameObject("BoscaliStrategyWindow", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var view = go.AddComponent<StrPlanningWindow>();
            view.war = war;
            view.overlay = overlay;
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
            if (!IsOpen) return;
            IsOpen = false;
            closedFrame = Time.frameCount;
            window.Hide();
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
        }

        private void OnDestroy() => Close();

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            war?.Refresh();
            Refresh();
        }

        private void Build()
        {
            window = AvWindow.Build(transform, "operations-room", "THEATER / OPERATIONS ROOM", Width, Height, SortOrder);

            AvFlow body = window.Body;
            body.Section(AvIcon.Map2, "LIVE THEATER MAP", "FRONT / CONTROL / STAFF INTENT");
            map = body.Add(new TheaterMapPart(body.Content, overlay));

            body.Section(AvIcon.Flag, "PRIMARY OPERATION", "STAFF DIRECTED");
            operationRow = body.Add(new AvRow(body.Content));
            body.Buttons(new AvControl.Spec("CALL OFF / REPLAN", CancelOperation, AvButtonStyle.Danger, AvIcon.X));

            body.Section(AvIcon.ListDetails, "STAFF PROPOSALS", "CHOOSE OR STAFF DECIDES");
            proposals = body.Add(new AvRowStack(body.Content, MaxProposals, Pick));

            body.Section(AvIcon.MapPin, "FRONTS", "FIELD REPORTS");
            fronts = body.Add(new AvList(body.Content, window.Ticker, MaxFronts, BindFront));

            body.Section(AvIcon.AdjustmentsHorizontal, "STAFF POSTURE", "BROAD INTENT");
            postureControl = body.Add(new AvSegmented(body.Content, "POSTURE",
                new[] { "CAUTIOUS", "STEADY", "BOLD" }, () => (int)selectedPosture, i => SetPosture((TheaterWarPosture)i)));

            window.Footer.Set("Staff log · awaiting report.");
        }

        private void BindFront(int index, AvRow row)
        {
            IReadOnlyList<TheaterFrontView> list = war?.Available == true ? war.Fronts : null;
            TheaterFrontView front = list != null && index < list.Count ? list[index] : null;
            if (front == null) { row.Set("—", "", "", AvState.Inert); return; }
            row.Set(front.Label, front.Observed
                    ? front.Status + " · " + TheaterReadout.Percent(Mathf.Clamp01(front.Pressure)) + " PRESSURE"
                    : "RUMOR / UNCONFIRMED",
                "", front.Observed ? AvState.Caution : AvState.Inert);
        }

        private void Refresh()
        {
            bool ready = war != null && war.Available;
            IReadOnlyList<TheaterProposalView> proposalList = ready ? war.Proposals : null;
            int count = proposalList != null ? Mathf.Min(proposalList.Count, MaxProposals) : 0;
            for (int i = 0; i < MaxProposals; i++)
            {
                if (i >= count) { proposals.Hide(i); continue; }
                TheaterProposalView proposal = proposalList[i];
                proposals.Show(i);
                AvRow row = proposals.Row(i);
                row.Set(proposal.Kind + " / " + proposal.Label,
                    proposal.Brief + "  ·  " + proposal.Forces + "  ·  RISK " + proposal.Risk,
                    Mathf.CeilToInt(Mathf.Max(0f, proposal.SecondsRemaining)) + "S", AvState.Ready);
                row.Interactable = war.CanCommand;
            }

            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;
            operationRow.Set(active == null ? "NO PRIMARY OPERATION" : active.Label,
                active == null ? "The staff is monitoring several fronts for an opening."
                    : active.Kind + " / " + active.Phase + " · " + active.Summary + "  ·  " +
                      active.GroundGroups + " GROUND / " + active.AirGroups + " AIR / " + active.NavalGroups + " NAVAL",
                "", AvState.Info);

            IReadOnlyList<TheaterFrontView> frontList = ready ? war.Fronts : null;
            fronts.SetCount(frontList?.Count ?? 0);

            selectedPosture = ready ? war.Posture : TheaterWarPosture.Steady;
            postureControl.Refresh();

            IReadOnlyList<string> log = ready ? war.StaffLog : null;
            window.Footer.Set(log != null && log.Count > 0 ? "Staff log · " + log[0] : "Staff log · no recent report.");

            window.Body.Relayout();
            map.Refresh(active, proposalList, frontList);
        }

        private void Pick(int slot)
        {
            IReadOnlyList<TheaterProposalView> proposalList = war?.Proposals;
            if (proposalList == null || slot < 0 || slot >= proposalList.Count) return;
            TheaterProposalView proposal = proposalList[slot];
            if (war.RequestPick(proposal.Id, proposal.Revision)) Refresh();
        }

        private void CancelOperation()
        {
            TheaterLiveOperationView active = war?.ActiveOperation;
            if (active != null && war.RequestCancel(active.Id, active.Revision)) Refresh();
        }

        private void SetPosture(TheaterWarPosture posture)
        {
            if (war != null && war.RequestPosture(posture)) Refresh();
        }

        /// <summary>
        /// The live theater map: terrain sprite, control-field overlay and front trace are genuinely
        /// data (spec §9.2 data-viz exception), so they stay as-is here — hosted inside a kit v2
        /// <see cref="AvPart"/> rather than the room's own bespoke frame.
        /// </summary>
        private sealed class TheaterMapPart : AvPart
        {
            private readonly ComMapOverlay overlay;
            private readonly Image terrain;
            private readonly RawImage control;
            private readonly RoomFrontlineGraphic frontGraphic;
            private readonly Image[] frontMarkers = new Image[MaxFronts];
            private readonly Image[] proposalMarkers = new Image[MaxProposals];
            private readonly Image activeMarker;
            private readonly TMP_Text status;

            public TheaterMapPart(RectTransform parent, ComMapOverlay mapOverlay)
            {
                overlay = mapOverlay;
                Rect = AvLay.Child(parent, "TheaterMap");
                AvFrame frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                frame.Paint(AvStyleHost.Resolve(AvStyleHost.FuiStyle("card").Background, AvTheme.SurfaceInert),
                            AvStyleHost.Resolve(AvStyleHost.FuiStyle("card").Border, AvTheme.Hairline));

                terrain = Image("Terrain");
                control = RawImg();
                var frontGo = new GameObject("Front", typeof(RectTransform), typeof(CanvasRenderer));
                frontGo.transform.SetParent(Rect, false);
                frontGraphic = frontGo.AddComponent<RoomFrontlineGraphic>();
                frontGraphic.raycastTarget = false;
                AvLay.Fill(frontGraphic.rectTransform, 2f);

                Color caution = AvStyleHost.FuiColor("caution", AvTheme.Warning);
                Color ready = AvStyleHost.FuiColor("ready", AvTheme.Accent);
                Color info = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
                for (int i = 0; i < frontMarkers.Length; i++) frontMarkers[i] = Marker(caution);
                for (int i = 0; i < proposalMarkers.Length; i++) proposalMarkers[i] = Marker(ready);
                activeMarker = Marker(info);

                status = AvText.Make(Rect, "Status", AvTextRole.Micro, "", TextAlignmentOptions.BottomLeft);
            }

            private Image Image(string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                AvLay.Fill(img.rectTransform, 2f);
                return img;
            }

            private RawImage RawImg()
            {
                var go = new GameObject("Control", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                var img = go.AddComponent<RawImage>();
                img.raycastTarget = false;
                AvLay.Fill(img.rectTransform, 2f);
                return img;
            }

            private Image Marker(Color color)
            {
                var go = new GameObject("Marker", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.enabled = false;
                img.color = color;
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                return img;
            }

            public override float Measure(float width) => Mathf.Round(width * 0.5f) + 16f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(status.rectTransform, 4f, s.H - 14f, s.W - 8f, 14f);
            }

            public void Refresh(TheaterLiveOperationView active, IReadOnlyList<TheaterProposalView> proposalList,
                IReadOnlyList<TheaterFrontView> frontList)
            {
                DynamicMap dynMap = SceneSingleton<DynamicMap>.i;
                Image source = dynMap?.mapImage?.GetComponent<Image>();
                terrain.sprite = source != null ? source.sprite : null;
                terrain.enabled = terrain.sprite != null;
                Transform controlXform = dynMap?.mapImage?.transform.Find("ComSectorGridOverlay");
                RawImage sourceControl = controlXform != null ? controlXform.GetComponent<RawImage>() : null;
                control.texture = sourceControl != null ? sourceControl.texture : null;
                control.enabled = control.texture != null;
                var grid = overlay?.Grid;
                frontGraphic.SetSource(grid);
                status.text = terrain.enabled
                    ? "LIVE TERRAIN · AMBER: FRONT · GREEN: OFFER · CYAN: ACTIVE"
                    : "NATIVE MAP IMAGE UNAVAILABLE";

                for (int i = 0; i < frontMarkers.Length; i++)
                {
                    TheaterFrontView front = frontList != null && i < frontList.Count ? frontList[i] : null;
                    PlaceMarker(frontMarkers[i], front != null && front.Observed, front?.X ?? float.NaN, front?.Z ?? float.NaN, grid);
                }
                for (int i = 0; i < proposalMarkers.Length; i++)
                {
                    TheaterProposalView proposal = proposalList != null && i < proposalList.Count ? proposalList[i] : null;
                    PlaceMarker(proposalMarkers[i], proposal != null, proposal?.X ?? float.NaN, proposal?.Z ?? float.NaN, grid);
                }
                PlaceMarker(activeMarker, active != null, active?.X ?? float.NaN, active?.Z ?? float.NaN, grid);
            }

            private void PlaceMarker(Image marker, bool known, float x, float z,
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
                if (u < 0f || u > 1f || v < 0f || v > 1f) { marker.enabled = false; return; }
                Rect r = Rect.rect;
                AvLay.Place(marker.rectTransform, u * r.width - 6f, (1f - v) * r.height - 6f, 12f, 12f);
                marker.enabled = true;
            }
        }

        /// <summary>World-space front traces projected over the room's flat native map sprite. Data-viz: kept.</summary>
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
