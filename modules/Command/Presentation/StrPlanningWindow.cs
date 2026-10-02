using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>A live theater map with staff proposals and the operation's actual state.</summary>
    internal sealed class StrPlanningWindow : MonoBehaviour
    {
        private const float Width = 900f;
        private const float Height = 980f;
        private const int SortOrder = 30001;
        private const int MaxProposals = 3;
        private const int MaxFronts = 4;

        private AvWindow window;
        private StrOpCard operationCard;
        private StrNote operationNote;
        private AvButtons callOff;
        private StrProposalDeck proposals;
        private StrNote proposalNote;
        private AvSection frontsSection;
        private StrFrontBoard fronts;
        private StrNote frontsNote;
        private AvSegmented postureControl;
        private AvSection logSection;
        private StrLogBoard staffLog;
        private StrNote staffLogNote;
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
            window.Body.Relayout();   // the first frame after opening must already be laid out
            ScrollRect scroll = window.Root.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;   // always open on the map
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
            // The window is a fixed size, so the flow is told its viewport: the map soaks up what the two
            // rows of cards under it leave, and nothing ends in a blank band.
            body.ViewportHeight = Height - 30f - AvGridTokens.Footer;
            body.Section(AvIcon.Map2, "LIVE THEATER MAP");
            map = body.Add(new TheaterMapPart(body.Content, overlay), 1f);

            // Two columns under the map keep the room on one screen: the operation (with the staff's posture)
            // beside the staff's offers, then the fronts beside the staff log.
            var opStack = new StrStack(body.Content);
            operationCard = opStack.Add(new StrOpCard(body.Content));
            operationNote = opStack.Add(new StrNote(body.Content, AvIcon.Flag));
            callOff = opStack.Add(new AvButtons(body.Content,
                new[] { new AvControl.Spec("CALL OFF / REPLAN", CancelOperation, AvButtonStyle.Danger, AvIcon.X) }));
            callOff.Controls[0].Help = "Call off this operation and request fresh staff choices.";
            postureControl = opStack.Add(new AvSegmented(body.Content, "POSTURE",
                new[] { "CAUTIOUS", "STEADY", "BOLD" }, () => (int)selectedPosture, i => SetPosture((TheaterWarPosture)i)));
            for (int i = 0; i < 3; i++) postureControl.Options[i].Help = StrMfdPanel.PostureBrief((TheaterWarPosture)i);

            var offerStack = new StrStack(body.Content);
            proposals = offerStack.Add(new StrProposalDeck(body.Content, MaxProposals, Pick));
            proposalNote = offerStack.Add(new StrNote(body.Content, AvIcon.ListDetails));

            var frontStack = new StrStack(body.Content);
            fronts = frontStack.Add(new StrFrontBoard(body.Content, MaxFronts, BindFront));
            frontsNote = frontStack.Add(new StrNote(body.Content, AvIcon.MapPin));

            var logStack = new StrStack(body.Content);
            staffLog = logStack.Add(new StrLogBoard(body.Content, 8, null, 3));
            staffLog.Grow = 1f;
            staffLogNote = logStack.Add(new StrNote(body.Content, AvIcon.ListDetails));
            staffLogNote.Grow = 1f;

            body.Row(new AvSection(body.Content, AvIcon.Flag, "OPERATION"),
                new AvSection(body.Content, AvIcon.ListDetails, "PROPOSALS", "PICK"));
            body.Row(opStack, offerStack);
            frontsSection = new AvSection(body.Content, AvIcon.MapPin, "FRONTS");
            logSection = new AvSection(body.Content, AvIcon.ListDetails, "STAFF LOG", "NEWEST FIRST");
            body.Row(frontsSection, logSection);
            body.Row(frontStack, logStack);

            AvControl closeButton = window.Root.GetComponentInChildren<AvControl>(true);
            if (closeButton != null) closeButton.Help = "Close operations room (Esc).";
            window.Footer.Set("Staff log · awaiting report.");
        }

        private void BindFront(int index, StrFrontBoard.Row row)
        {
            IReadOnlyList<TheaterFrontView> list = war?.Available == true ? war.Fronts : null;
            StrMfdPanel.FillFront(row, list != null && index < list.Count ? list[index] : null);
        }

        private void Refresh()
        {
            bool ready = war != null && war.Available;
            IReadOnlyList<TheaterProposalView> proposalList = ready ? war.Proposals : null;
            int count = proposalList != null ? Mathf.Min(proposalList.Count, MaxProposals) : 0;
            proposals.SetShown(count > 0);
            proposalNote.SetShown(count == 0);
            if (count > 0) StrMfdPanel.FillProposals(proposals, proposalList, MaxProposals, war.CanCommand);
            else
                proposalNote.Set(ready ? "NO OPENINGS" : "NO PROPOSALS", "");

            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;
            operationCard.SetShown(active != null);
            operationNote.SetShown(active == null);
            callOff.SetShown(active != null);
            if (active != null)
            {
                operationCard.Set(active.Kind, active.Label, active.Phase, active.Summary,
                    active.GroundGroups, active.AirGroups, active.NavalGroups);
                callOff.Controls[0].Interactable = war.CanCommand;
            }
            else
                operationNote.Set(ready ? "NO OPERATION" : "STAFF OFFLINE", "");

            IReadOnlyList<TheaterFrontView> frontList = ready ? war.Fronts : null;
            int frontCount = frontList?.Count ?? 0;
            fronts.SetCount(frontCount);
            fronts.SetShown(frontCount > 0);
            frontsNote.SetShown(frontCount == 0);
            frontsSection.SetCaption(frontCount == 0 ? "NO REPORTS" : frontCount + " TRACKED");
            if (frontCount == 0)
                frontsNote.Set("NO FRONTS", "");

            selectedPosture = ready ? war.Posture : TheaterWarPosture.Steady;
            postureControl.Refresh();

            IReadOnlyList<string> log = ready ? war.StaffLog : null;
            int lines = log == null ? 0 : Mathf.Min(log.Count, staffLog.Capacity);
            staffLog.Begin();
            for (int i = 0; i < lines; i++)
                staffLog.Add((i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture), log[i],
                    AvState.Info, false, "Staff log entry " + (i + 1) + ", newest first.");
            staffLog.End();
            staffLog.SetShown(lines > 0);
            staffLogNote.SetShown(lines == 0);
            if (lines == 0) staffLogNote.Set(ready ? "NO STAFF TRAFFIC" : "STAFF OFFLINE", "");
            window.Footer.Set(lines > 0 ? "Staff log · " + log[0] : "Staff log · no recent report.");

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

            public override float Measure(float width) => Mathf.Round(width * 0.34f) + 16f;

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
                BoscaliSummer.Modules.Command.Runtime.TacticalSectorGrid grid)
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
            private BoscaliSummer.Modules.Command.Runtime.TacticalSectorGrid source;
            private ulong drawnHash;

            public void SetSource(BoscaliSummer.Modules.Command.Runtime.TacticalSectorGrid grid)
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
