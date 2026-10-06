using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>A live theater map with staff proposals and the operation's actual state.</summary>
    internal sealed class StrPlanningWindow : MonoBehaviour
    {
        // 720p reference keeps the kit's 11px floor at 11 screen pixels on the smallest supported room.
        private const float Width = 1220f;
        private const float Height = 700f;
        private const float FooterHeight = 36f;
        private const int SortOrder = 30001;
        private const int MaxProposals = 3;
        private const int MaxFronts = 8;
        private const int FrontPageSize = 4;

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
        private StrNote staffState, postureNote;
        private AvSection proposalSection;
        private int paintedActiveId, paintedActiveRevision;
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
            scaler.referenceResolution = new Vector2(1280f, 720f);
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
            window?.Hide();
            ReleaseInput();
        }

        private void ReleaseInput()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closedFrame = Time.frameCount;
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
        }

        private void OnDisable() => Close();
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
            window.Closed += ReleaseInput;

            AvFlow body = window.Body;
            // Keep the state and choices on one 720p screen; the front board pages the eight reports.
            body.ViewportHeight = Height - 30f - FooterHeight;
            // The room's status/help is two short lines. Reuse the footer at a compact height so offers stay in view.
            RectTransform bodyRect = (RectTransform)window.Root.Find("Body");
            ScrollRect roomScroll = bodyRect.GetComponent<ScrollRect>();
            AvLay.Place(bodyRect, 0f, 30f, Width, body.ViewportHeight);
            AvLay.Place(roomScroll.viewport, 0f, 0f, Width, body.ViewportHeight);
            AvLay.Place((RectTransform)roomScroll.verticalScrollbar.transform, Width - AvGridTokens.Pad - AvGridTokens.Gutter + 2f, 2f, 4f, body.ViewportHeight - 4f);
            window.Footer.Place(new AvSlot(0f, Height - FooterHeight, Width, FooterHeight));
            staffState = body.Add(new StrNote(body.Content, AvIcon.Radio));

            var picture = new StrStack(body.Content);
            picture.Add(new AvSection(body.Content, AvIcon.Map2, "THEATER PICTURE", "ACT ACTIVE / O OFFER / F FRONT"));
            map = picture.Add(new TheaterMapPart(body.Content, overlay));

            // Map/front reports/posture on the left; the active aim and all three choices on the right.
            var opStack = new StrStack(body.Content);
            opStack.Add(new AvSection(body.Content, AvIcon.Flag, "PRIMARY OPERATION", "ACT"));
            operationCard = opStack.Add(new StrOpCard(body.Content));
            operationNote = opStack.Add(new StrNote(body.Content, AvIcon.Flag));
            callOff = opStack.Add(new AvButtons(body.Content,
                new[] { new AvControl.Spec("CALL OFF / REPLAN", CancelOperation, AvButtonStyle.Danger, AvIcon.X) }));
            callOff.Controls[0].Help = "Call off this operation and request fresh staff choices.";
            postureControl = new AvSegmented(body.Content, "POSTURE",
                new[] { "CAUTIOUS", "STEADY", "BOLD" },
                () => war?.Available == true && war.HasSnapshot ? (int)selectedPosture : -1,
                i => SetPosture((TheaterWarPosture)i));
            for (int i = 0; i < 3; i++) postureControl.Options[i].Help = StrMfdPanel.PostureBrief((TheaterWarPosture)i);
            postureNote = new StrNote(body.Content, AvIcon.Flag);
            proposalSection = opStack.Add(new AvSection(body.Content, AvIcon.ListDetails, "STAFF PROPOSALS"));
            proposals = opStack.Add(new StrProposalDeck(body.Content, MaxProposals, Pick));
            proposalNote = opStack.Add(new StrNote(body.Content, AvIcon.ListDetails));
            frontsSection = new AvSection(body.Content, AvIcon.MapPin, "FRONTS");
            logSection = new AvSection(body.Content, AvIcon.ListDetails, "STAFF LOG", "NEWEST FIRST");
            picture.Add(frontsSection);
            fronts = picture.Add(new StrFrontBoard(body.Content, FrontPageSize, BindFront));
            frontsNote = picture.Add(new StrNote(body.Content, AvIcon.MapPin));
            picture.Add(postureControl);
            picture.Add(postureNote);
            // Full staff traffic remains on STR; the room footer carries the latest report.
            staffLog = new StrLogBoard(body.Content, 8, null, 1);
            staffLogNote = new StrNote(body.Content, AvIcon.ListDetails);
            logSection.SetShown(false);
            staffLog.Rect.gameObject.SetActive(false);
            staffLogNote.Rect.gameObject.SetActive(false);
            body.Row(picture, opStack);

            AvControl closeButton = window.CloseControl;
            if (closeButton != null) closeButton.Help = "Close operations room (Esc).";
            window.Footer.Set("Staff log · awaiting report.");
        }

        private void BindFront(int index, StrFrontBoard.Row row)
        {
            IReadOnlyList<TheaterFrontView> list = war?.Available == true ? war.Fronts : null;
            StrMfdPanel.FillFront(row, list != null && index < list.Count ? list[index] : null, index);
        }

        private void Refresh()
        {
            bool ready = war != null && war.Available && war.HasSnapshot;
            StrMfdPanel.FillStaffState(staffState, war);
            IReadOnlyList<TheaterProposalView> proposalList = ready ? war.Proposals : null;
            int count = proposalList != null ? Mathf.Min(proposalList.Count, MaxProposals) : 0;
            proposals.SetShown(count > 0);
            proposalNote.SetShown(count == 0);
            if (count > 0) StrMfdPanel.FillProposals(proposals, proposalList, MaxProposals, war.CanCommand, war.SnapshotAgeSeconds <= 15f);
            else
                proposalNote.Set(ready ? "NO ELIGIBLE OPENINGS" : "NO PROPOSALS", "Staff assesses observed fronts and nearby forces.");
            proposalSection.SetCaption(StrMfdPanel.AutoSelectionCaption(proposalList, ready && war.SnapshotAgeSeconds <= 15f));

            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;
            operationCard.SetShown(active != null);
            operationNote.SetShown(active == null);
            callOff.SetShown(active != null);
            if (active != null)
            {
                paintedActiveId = active.Id;
                paintedActiveRevision = active.Revision;
                operationCard.Set(active.Kind, active.Label, active.Phase, active.Summary,
                    active.GroundGroups, active.AirGroups, active.NavalGroups);
                callOff.Controls[0].Interactable = war.CanCommand;
            }
            else
            {
                paintedActiveId = paintedActiveRevision = 0;
                operationNote.Set(ready ? "NO OPERATION" : "AWAITING STAFF", ready ? "" : "Host report required.");
            }

            IReadOnlyList<TheaterFrontView> frontList = ready ? war.Fronts : null;
            int frontCount = frontList?.Count ?? 0;
            fronts.SetCount(frontCount);
            fronts.SetShown(frontCount > 0);
            frontsNote.SetShown(frontCount == 0);
            frontsSection.SetCaption(frontCount == 0 ? "NO REPORTS" : frontCount + " TRACKED");
            if (frontCount == 0)
                frontsNote.Set(ready ? "NO FRONTS" : "FRONT REPORT UNAVAILABLE", ready ? "" : "Awaiting the host's faction report.");

            selectedPosture = ready ? war.Posture : TheaterWarPosture.Steady;
            postureControl.Refresh();
            foreach (AvControl option in postureControl.Options) option.Interactable = ready && war.CanCommand;
            postureNote.Set(ready ? "POSTURE · " + selectedPosture.ToString().ToUpperInvariant() : "POSTURE UNAVAILABLE",
                ready ? StrMfdPanel.PostureBrief(selectedPosture) : "Awaiting the host's faction report.", ready ? AvState.Info : AvState.Inert);

            IReadOnlyList<string> log = ready ? war.StaffLog : null;
            int lines = log == null ? 0 : Mathf.Min(log.Count, staffLog.Capacity);
            staffLog.Begin();
            for (int i = 0; i < lines; i++)
                staffLog.Add((i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture), log[i],
                    AvState.Info, false, "Staff log entry " + (i + 1) + ", newest first.");
            staffLog.End();
            staffLog.SetShown(false);
            staffLogNote.SetShown(false);
            if (lines == 0) staffLogNote.Set(ready ? "NO STAFF TRAFFIC" : "STAFF OFFLINE", "");
            window.Footer.Set(!ready ? "Staff log · awaiting host report." : lines > 0 ? "Staff log · " + log[0] : "Staff log · no recent report.");

            map.Refresh(active, proposalList, frontList);
        }

        private void Pick(int id, int revision)
        {
            if (war == null || !war.CanCommand) return;
            war.RequestPick(id, revision);
            Refresh();
        }

        private void CancelOperation()
        {
            if (war == null || !war.CanCommand || paintedActiveId <= 0) return;
            war.RequestCancel(paintedActiveId, paintedActiveRevision);
            Refresh();
        }

        private void SetPosture(TheaterWarPosture posture)
        {
            if (war == null || !war.CanCommand) return;
            war.RequestPosture(posture);
            Refresh();
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
            private readonly TMP_Text[] frontLabels = new TMP_Text[MaxFronts];
            private readonly TMP_Text[] proposalLabels = new TMP_Text[MaxProposals];
            private readonly TMP_Text activeLabel;
            private readonly TMP_Text status;

            public TheaterMapPart(RectTransform parent, ComMapOverlay mapOverlay)
            {
                overlay = mapOverlay;
                Rect = AvLay.Child(parent, "TheaterMap");
                AvFrame frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(frame.rectTransform);
                frame.Paint(AvStyleHost.FuiFill("card", AvTheme.SurfaceInert),
                            AvStyleHost.FuiBorder("card", AvTheme.Hairline));

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
                for (int i = 0; i < frontMarkers.Length; i++) { frontMarkers[i] = Marker(caution); frontLabels[i] = PinLabel("F" + (i + 1), caution); }
                for (int i = 0; i < proposalMarkers.Length; i++) { proposalMarkers[i] = Marker(ready); proposalLabels[i] = PinLabel("O" + (i + 1), ready); }
                activeMarker = Marker(info);
                activeLabel = PinLabel("ACT", info);

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

            private TMP_Text PinLabel(string tag, Color color)
            {
                RectTransform box = AvLay.Child(Rect, "Pin " + tag);
                AvFrame back = AvFrame.Add(box, "Frame", AvChamfer.Diagonal(3f));
                AvLay.Fill(back.rectTransform);
                back.Paint(AvInk.Raised, color);
                TMP_Text text = AvText.Make(box, "Tag " + tag, AvTextRole.DataStrong, tag, TextAlignmentOptions.Center);
                AvText.Fit(text, false);
                AvLay.Fill(text.rectTransform, 1f);
                text.color = AvInk.Ink;
                box.gameObject.SetActive(false);
                return text;
            }

            public override float Measure(float width) => Mathf.Round(width * 0.30f) + 20f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(terrain.rectTransform, 2f, 2f, s.W - 4f, s.H - 24f);
                AvLay.Place(control.rectTransform, 2f, 2f, s.W - 4f, s.H - 24f);
                AvLay.Place(frontGraphic.rectTransform, 2f, 2f, s.W - 4f, s.H - 24f);
                AvLay.Place(status.rectTransform, 6f, s.H - 17f, s.W - 12f, 16f);
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
                    ? "ACT ACTIVE · O1–3 OFFERS · F1–8 OBSERVED ONLY"
                    : "NATIVE MAP IMAGE UNAVAILABLE";

                for (int i = 0; i < frontMarkers.Length; i++)
                {
                    TheaterFrontView front = frontList != null && i < frontList.Count ? frontList[i] : null;
                    PlaceMarker(frontMarkers[i], frontLabels[i], front != null && front.Observed, front?.X ?? float.NaN, front?.Z ?? float.NaN, grid, 0f);
                }
                for (int i = 0; i < proposalMarkers.Length; i++)
                {
                    TheaterProposalView proposal = proposalList != null && i < proposalList.Count ? proposalList[i] : null;
                    PlaceMarker(proposalMarkers[i], proposalLabels[i], proposal != null, proposal?.X ?? float.NaN, proposal?.Z ?? float.NaN, grid, -22f);
                }
                PlaceMarker(activeMarker, activeLabel, active != null, active?.X ?? float.NaN, active?.Z ?? float.NaN, grid, 22f);
            }

            private void PlaceMarker(Image marker, TMP_Text tag, bool known, float x, float z,
                BoscaliSummer.Modules.Command.Runtime.TacticalSectorGrid grid, float offsetY)
            {
                if (!known || grid == null || !(grid.WorldSizeX > 0f) || !(grid.WorldSizeY > 0f) ||
                    float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z))
                {
                    marker.enabled = false;
                    tag.transform.parent.gameObject.SetActive(false);
                    return;
                }
                float u = x / grid.WorldSizeX + .5f;
                float v = z / grid.WorldSizeY + .5f;
                if (u < 0f || u > 1f || v < 0f || v > 1f) { marker.enabled = false; tag.transform.parent.gameObject.SetActive(false); return; }
                Rect r = Rect.rect;
                float px = 2f + u * (r.width - 4f), py = 2f + (1f - v) * (r.height - 24f);
                AvLay.Place(marker.rectTransform, px - 5f, py - 5f, 10f, 10f);
                float tagWidth = tag.text == "ACT" ? 36f : 28f;
                AvLay.Place((RectTransform)tag.transform.parent, Mathf.Clamp(px + 8f, 3f, r.width - tagWidth - 3f), Mathf.Clamp(py - 10f + offsetY, 3f, r.height - 43f), tagWidth, 20f);
                tag.transform.parent.gameObject.SetActive(true);
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
