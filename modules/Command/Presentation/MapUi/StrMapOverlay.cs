using System.Diagnostics;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The STR layer inside the maximised map (annex 05 P10, frame C): an ink mesh for the shapes, a small pulse mesh for the marching axis, pooled tags, the click
    /// card, the layer plate and the legend. It reads the hub's model through <see cref="StrOverlayPlan"/> and nothing else, draws world positions through
    /// <see cref="IMapProjection"/> (the relief) or the flat sheet's own scale, and keeps every tag 20 px clear of the icon it names. It runs after the relief's
    /// pose of the frame (execution order 101), and does nothing while the map is closed.
    /// </summary>
    [DefaultExecutionOrder(101)]
    internal sealed partial class StrMapOverlay : MonoBehaviour, ISceneService
    {
        internal const int InkCapacity = 900, PulseCapacity = 120;
        private const float PoseSeconds = 0.066f, PulseSeconds = 1f / 15f, WindowSeconds = 5f, LeaderClear = 14f, ViewMargin = 6f;
        private const int RingSegments = 48;

        /// <summary>The scene's overlay, for the automation hooks and the harness.</summary>
        internal static StrMapOverlay Active { get; private set; }

        internal IStrHub Hub => StrViewHub.Active;

        internal StrOverlayPlan Plan { get; } = new StrOverlayPlan();
        internal StrCardPlan CardPlan { get; } = new StrCardPlan();
        /// <summary>The layer toggles and the card's buttons by control id (layer.*, map.card.*).</summary>
        internal StrControls Controls { get; } = new StrControls();

        /// <summary>Counters for the sim read-back and the harness: rebuilds, quads written, overflow, the last rebuild's cost and the cost per second over a 5 s window.</summary>
        internal int Rebuilds, InkQuads, PulseQuads;
        internal bool Overflowed, Visible, Relief;
        internal float LastRebuildMs, MsPerSecond, RebuildsPerSecond;

        private readonly RebuildGate gate = new RebuildGate(PoseSeconds);
        // The relief commits a pose only when it renders the terrain, so its revision is already rate-limited: follow every one, or the layer trails the ground.
        private readonly RebuildGate reliefGate = new RebuildGate(0f);
        private readonly System.Collections.Generic.List<StrMapTag> tags = new System.Collections.Generic.List<StrMapTag>(StrOverlayPlan.MaxTags);
        private readonly float[] ax = new float[StrOverlayPlan.MaxTags], ay = new float[StrOverlayPlan.MaxTags], tw = new float[StrOverlayPlan.MaxTags], th = new float[StrOverlayPlan.MaxTags];
        private readonly RectF[] placed = new RectF[StrOverlayPlan.MaxTags];
        private readonly Vector2[] anchorLocal = new Vector2[StrOverlayPlan.MaxTags];
        private readonly float[] ringX = new float[RingSegments], ringY = new float[RingSegments];
        private readonly bool[] ringOk = new bool[RingSegments];
        private readonly Vector3[] corners = new Vector3[4];
        // The marching offensive, its centre line kept projected so the pulse mesh can redraw at 15 Hz without projecting again.
        private const int MaxPulse = 2;
        private readonly float[][] pulseCurveX = { new float[StrStrokes.MaxCurve], new float[StrStrokes.MaxCurve] };
        private readonly float[][] pulseCurveY = { new float[StrStrokes.MaxCurve], new float[StrStrokes.MaxCurve] };
        private readonly int[] pulseN = new int[MaxPulse];
        private readonly float[] pulseSize = new float[MaxPulse];
        private int pulseCount;
        private float pulsePx = 1f, nextPulse;
        private readonly float[] curveX = new float[StrStrokes.MaxCurve], curveY = new float[StrStrokes.MaxCurve];
        // Who holds the ground, for where an offensive jumps off: the local faction's sector field, read once per plan.
        private TacticalSectorGrid startGrid;
        private StrStartFinder startFinder;
        private System.Func<float, float, int> sideOf;
        // Own ground vehicles, for the garrison an offensive jumps off from.
        private const int MaxGarrisonUnits = 256;
        private readonly float[] garrisonX = new float[MaxGarrisonUnits], garrisonZ = new float[MaxGarrisonUnits];
        private int garrisonCount;

        private RectTransform root, tagLayer;
        private AvVector ink, pulse;
        private StrMapChrome chrome;
        private StrMapCard card;
        private StrViewModel planModel;
        private int planRevision = -1;
        private StrLayers planLayers = StrLayers.None;
        private StrItemId planSelected, planHover;
        private bool cardDirty, warnedShape;
        // A tag's words and size change only with the plan or the map's scale; a pan or an orbit just moves it. Refilling every tag on every pose was most of a rebuild.
        private bool tagsStale = true;
        private float tagsPx;
        private float windowStart = -1f, windowMs;
        private int windowRebuilds;

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            if (Active == this) Active = null;
            Teardown();
        }

        private void OnDestroy() => OnDisable();

        public void ResetForScene() => Teardown();

        private void LateUpdate()
        {
            Tick(Time.unscaledTime);
            if (Visible && !Application.isBatchMode && Input.GetKeyDown(KeyCode.Escape))
            {
                IStrHub hub = Hub;
                if (DrawArmed) CancelDraw("DRAW CANCELLED");
                else if (hub != null && !hub.Ui.Selected.IsNone) hub.ClearSelection();
            }
        }

        /// <summary>Brings the overlay up to date now: the sim presses and the harness call this so a press never meets a stale card.</summary>
        internal void RefreshNow() => Tick(Time.unscaledTime);

        /// <summary>One frame of the overlay. Cheap when nothing changed: it compares the model's revision, the layers and the map's pose and builds only on a difference.</summary>
        internal void Tick(float now)
        {
            if (!MissionProfile.AllowsModule("strategic-command")) { Hide(); return; }
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            IStrHub hub = Hub;
            if (hub == null || map == null || map.mapImage == null || map.mapBackground == null || !DynamicMap.mapMaximized) { Hide(); return; }
            if (root == null && !Build(map)) return;

            StrViewModel vm = hub.Current;
            StrUiState ui = hub.Ui;
            StrLayers layers = (ui.ShowFronts ? StrLayers.Fronts : 0) | (ui.ShowOperation ? StrLayers.Operation : 0) | (ui.ShowSorties ? StrLayers.Sorties : 0);
            if (!Visible) { Visible = true; chrome.SetShown(true); planRevision = -1; gate.Reset(); reliefGate.Reset(); }
            bool worldOn = layers != StrLayers.None;
            if (root.gameObject.activeSelf != worldOn) root.gameObject.SetActive(worldOn);

            bool planChanged = !ReferenceEquals(vm, planModel) || vm.Revision != planRevision || layers != planLayers || vm.SelectedId != planSelected || vm.HoverId != planHover;
            long t0 = Stopwatch.GetTimestamp();
            if (planChanged)
            {
                planModel = vm; planRevision = vm.Revision; planLayers = layers; planSelected = vm.SelectedId; planHover = vm.HoverId;
                startGrid = OwnGrid(map);
                garrisonCount = OwnGround(map);
                StrOverlayPlan.Build(Plan, vm, layers, startGrid != null || garrisonCount > 0 ? startFinder ?? (startFinder = FindStart) : null);
                StrCardPlan.Build(CardPlan, vm, worldOn ? vm.SelectedId : StrItemId.None);
                cardDirty = true;
                chrome.BindPlate(vm, in ui);
            }
            IMapProjection projection = ModuleServices.TryGet(out IMapProjection p) && p.IsActive ? p : null;
            Relief = projection != null;
            if (planChanged) chrome.BindLegend(Plan, map.mapBackground.rectTransform.rect.width);

            if (planChanged) tagsStale = true;
            bool poseChanged = (projection != null ? reliefGate : gate).ShouldRebuild(Pose(map, projection), now);
            bool rebuilt = false;
            if (worldOn && (planChanged || poseChanged)) rebuilt = Rebuild(map, projection, now, planChanged);
            else if (!worldOn) Plan.TagCount = 0;
            TickDraw(map, now);
            if (worldOn && drawHeld && !rebuilt)
            {
                DrawPulse(now);   // the draft follows the cursor every frame
                pulse.Commit();
                PulseQuads = pulse.Buffer.Count;
            }
            else if (worldOn && pulseCount > 0 && !AvFxDriver.ReducedMotion && now >= nextPulse && !rebuilt)
            {
                nextPulse = now + PulseSeconds;
                DrawPulse(now);
                pulse.Commit();
                PulseQuads = pulse.Buffer.Count;
            }
            if (!rebuilt && (planChanged || cardDirty)) UpdateCard(map, planChanged);   // a rebuild places the card itself, between the tags and the ink
            if (planChanged || rebuilt) Account(now, (Stopwatch.GetTimestamp() - t0) * 1000f / Stopwatch.Frequency, rebuilt);
            else if (windowStart < 0f) windowStart = now;
            if (windowStart >= 0f && now - windowStart >= WindowSeconds)
            {
                float span = now - windowStart;
                MsPerSecond = windowMs / span;
                RebuildsPerSecond = windowRebuilds / span;
                windowStart = now; windowMs = 0f; windowRebuilds = 0;
            }
        }

        private void Account(float now, float ms, bool rebuilt)
        {
            if (windowStart < 0f) windowStart = now;
            windowMs += ms;
            if (!rebuilt) return;
            windowRebuilds++;
            LastRebuildMs = ms;
        }

        private static int Pose(DynamicMap map, IMapProjection projection)
        {
            unchecked
            {
                Transform t = map.mapImage.transform;
                Vector3 at = t.position, scale = t.lossyScale;
                int h = projection != null ? projection.Revision * 31 + 1 : 0;
                // A flat map that follows the aircraft slides every frame; the tags ride on the image, so only a slide of a few pixels needs a new placement.
                h = h * 31 + Mathf.RoundToInt(at.x / 12f);
                h = h * 31 + Mathf.RoundToInt(at.y / 12f);
                return h * 31 + Mathf.RoundToInt(scale.x * 100f);
            }
        }

        // ---- lifecycle ------------------------------------------------------------------------------------------------------

        private bool Build(DynamicMap map)
        {
            RectTransform image = (RectTransform)map.mapImage.transform, viewport = map.mapBackground.rectTransform;
            root = Stretched("BoscaliStr.MapOverlay", image, image.pivot);
            ink = AvVector.Create(root, "Ink", InkCapacity);
            Pivot(ink.rectTransform, image.pivot);
            pulse = AvVector.Create(root, "Pulse", PulseCapacity);
            Pivot(pulse.rectTransform, image.pivot);
            tagLayer = Stretched("Tags", root, image.pivot);
            chrome = new StrMapChrome(viewport, this, Controls);
            card = new StrMapCard(viewport, this, Controls, CardPlan);
            MapUiPointer.Register(chrome.Plate);
            MapUiPointer.Register(chrome.Legend);
            MapUiPointer.Register(card.Root);
            return true;
        }

        private static RectTransform Stretched(string name, RectTransform parent, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            Pivot(rect, pivot);
            return rect;
        }

        // A stretched graphic's vertices are written relative to its pivot, so the pivot is the map image's: then they are in map-local units.
        private static void Pivot(RectTransform rect, Vector2 pivot)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            rect.pivot = pivot;
            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;
        }

        private void Hide()
        {
            if (DrawArmed) CancelDraw("");
            if (!Visible) return;
            Visible = false;
            if (root != null) root.gameObject.SetActive(false);
            chrome?.SetShown(false);
            card?.Hide();
        }

        // The scene unload may have destroyed the pieces already (Unity's null check says so): then there is nothing to remove or unregister.
        private static void Remove(Component part)
        {
            if (part == null) return;
            MapUiPointer.Unregister(part as RectTransform);
            part.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(part.gameObject); else DestroyImmediate(part.gameObject);
        }

        // No Hide() first: on a scene reset the unload has already destroyed the chrome and card, and Hide touches them without Unity's null check.
        // Remove hides each living piece itself, and Controls.Clear drops the card's buttons.
        private void Teardown()
        {
            Remove(root);
            if (chrome != null) { Remove(chrome.Plate); Remove(chrome.Legend); }
            if (card != null) Remove(card.Root);
            root = tagLayer = null; ink = pulse = null; chrome = null; card = null;
            tags.Clear();
            Controls.Clear();
            Plan.PrimCount = Plan.TagCount = Plan.LegendCount = 0;
            CardPlan.Valid = false;
            planModel = null; planRevision = -1; planLayers = StrLayers.None; planSelected = planHover = StrItemId.None;
            pulseCount = 0; cardDirty = false; warnedShape = false; startGrid = null; garrisonCount = 0;
            if (DrawArmed) CancelDraw("");
            Visible = false; Relief = false;
            tagsStale = true;
            gate.Reset(); reliefGate.Reset();
            windowStart = -1f; windowMs = 0f; windowRebuilds = 0;
        }

        // ---- the rebuild ----------------------------------------------------------------------------------------------------

        private bool Rebuild(DynamicMap map, IMapProjection projection, float now, bool rebind)
        {
            float factor = map.mapDisplayFactor, zoom = Mathf.Abs(map.mapImage.transform.localScale.x);
            if (!(zoom > 1e-4f) || (projection == null && !(factor > 1e-6f))) return false;
            float px = 1f / zoom;
            drawProjection = projection; drawFactor = factor;
            Camera camera = CameraOf(map);
            RectTransform viewport = map.mapBackground.rectTransform;
            Rect screen = ScreenRect(viewport, camera);
            // Tags stay clear of the legend strip along the bottom edge, which would hide them.
            float reserve = chrome.BottomReserve;
            screen = Rect.MinMaxRect(screen.xMin, screen.yMin + reserve, screen.xMax, screen.yMax);
            var view = new RectF(screen.x + ViewMargin, screen.y + ViewMargin, screen.width - 2f * ViewMargin, screen.height - 2f * ViewMargin);
            var image = (RectTransform)map.mapImage.transform;

            // Tags first: their sizes and places decide the leaders in the ink.
            int n = Plan.TagCount;
            bool fill = tagsStale || !Mathf.Approximately(px, tagsPx);
            tagsStale = false; tagsPx = px;
            for (int i = 0; i < n; i++)
            {
                StrTagSpec s = Plan.Tags[i];
                bool fresh = tags.Count <= i;
                StrMapTag tag = TagAt(i);
                bool ok = Project(projection, factor, s.X, s.Z, out float lx, out float ly);
                anchorLocal[i] = new Vector2(lx, ly);
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(camera, image.TransformPoint(new Vector3(lx, ly, 0f)));
                bool inside = ok && sp.x >= screen.xMin + ViewMargin && sp.x <= screen.xMax - ViewMargin && sp.y >= screen.yMin + ViewMargin && sp.y <= screen.yMax - ViewMargin;
                if (fill || fresh) tag.Show(in s, px, false);
                else if (!tag.gameObject.activeSelf) tag.gameObject.SetActive(true);
                tag.AnchorLocal = anchorLocal[i];
                ax[i] = inside ? sp.x : float.NaN; ay[i] = inside ? sp.y : float.NaN;
                // Every plate is placed at its resting size; the hovered one keeps the size it had before it opened.
                if (fill || fresh || !s.Hot) { Rect size = tag.ScreenBounds(camera); tw[i] = size.width; th[i] = size.height; }
                if ((fill || fresh) && s.Hot) tag.Show(in s, px, true);
            }
            StrTagPlacer.Place(in view, ax, ay, tw, th, n, placed);
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(ax[i])) { tags[i].Hide(); continue; }
                if (Plan.Tags[i].Hot)
                {
                    // The hovered plate opens where it rests, from its top-left corner and over its neighbours, so it never slides out from under the cursor.
                    Rect open = tags[i].ScreenBounds(camera);
                    placed[i] = new RectF(Mathf.Max(view.X, Mathf.Min(placed[i].X, view.X + view.W - open.width)), Mathf.Max(view.Y, placed[i].Y + placed[i].H - open.height), open.width, open.height);
                    tags[i].Root.SetAsLastSibling();
                }
                tags[i].PlaceAt(new Vector2(placed[i].X + placed[i].W * 0.5f, placed[i].Y + placed[i].H * 0.5f), camera);
            }
            for (int i = n; i < tags.Count; i++) tags[i].Hide();

            // The card owns the ground it covers: a plate left there draws through its words, so it rests until the card closes.
            UpdateCard(map, rebind);
            if (card.Visible)
            {
                Rect over = ScreenRect(card.Root, camera);
                for (int i = 0; i < n; i++)
                {
                    if (float.IsNaN(ax[i]) || Plan.Tags[i].Id == CardPlan.Id || !over.Overlaps(new Rect(placed[i].X, placed[i].Y, placed[i].W, placed[i].H))) continue;
                    tags[i].Hide();
                    ax[i] = float.NaN;
                }
            }

            // The ink: shapes in plan order, then one leader per visible tag.
            AvQuadBuffer b = ink.Buffer, pb = pulse.Buffer;
            b.Clear(); pb.Clear();
            pulseCount = 0; pulsePx = px;
            Rgba under = AvInk.Ground.WithAlpha(0.9f).ToRgba();
            for (int i = 0; i < Plan.PrimCount; i++) DrawPrim(in Plan.Prims[i], b, projection, factor, px, under);
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(ax[i])) continue;
                Vector2 start = new Vector2(ax[i], ay[i]), end = new Vector2(Mathf.Clamp(ax[i], placed[i].X, placed[i].X + placed[i].W), Mathf.Clamp(ay[i], placed[i].Y, placed[i].Y + placed[i].H));
                Vector2 line = end - start;
                if (line.sqrMagnitude <= LeaderClear * LeaderClear * 2f) continue;
                start += line.normalized * LeaderClear;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(ink.rectTransform, start, camera, out Vector2 a) ||
                    !RectTransformUtility.ScreenPointToLocalPointInRectangle(ink.rectTransform, end, camera, out Vector2 e)) continue;
                Rgba c = StrInk.Of(Plan.Tags[i].Ink).WithAlpha(0.6f).ToRgba();
                AvStrokes.Line(b, a.x, a.y, e.x, e.y, px, c);
            }
            DrawPulse(now);
            nextPulse = now + PulseSeconds;
            ink.Commit(); pulse.Commit();
            Rebuilds++;
            InkQuads = b.Count; PulseQuads = pb.Count;
            if ((b.Overflowed || pb.Overflowed) && !Overflowed)
            {
                Overflowed = true;
                Plugin.Logger?.LogWarning("[STR] the map overlay ran out of ink quads; the harness fails the run on this.");
            }
            return true;
        }

        private StrMapTag TagAt(int i)
        {
            while (tags.Count <= i) tags.Add(StrMapTag.Create(tagLayer, this));
            return tags[i];
        }

        private bool Project(IMapProjection projection, float factor, float wx, float wz, out float x, out float y)
        {
            if (projection != null) return projection.TryProject(wx, wz, out x, out y);
            x = wx * factor; y = wz * factor;
            return true;
        }

        private void DrawPrim(in StrPrim p, AvQuadBuffer b, IMapProjection projection, float factor, float px, Rgba under)
        {
            if (!Project(projection, factor, p.X, p.Z, out float x, out float y)) return;
            Rgba c = StrInk.RgbaOf(p.Ink);
            switch (p.Shape)
            {
                case StrShape.Cross:
                case StrShape.Diamond:
                case StrShape.DiamondSolid:
                case StrShape.Hex:
                case StrShape.Box:
                    StrShapeInk.Glyph(b, p.Shape, x, y, px, c, under);
                    break;
                case StrShape.Ring:
                case StrShape.DashedRing:
                    Ring(b, in p, projection, factor, x, y, px, c);
                    break;
                case StrShape.Arrow:
                case StrShape.DashedArrow:
                case StrShape.DashedLine:
                    if (!Project(projection, factor, p.X2, p.Z2, out float x2, out float y2)) return;
                    Line(b, p.Shape, x, y, x2, y2, 0f, px, c);
                    break;
                case StrShape.Offensive:
                    Offensive(b, in p, projection, factor, px, c);
                    break;
                default:
                    if (!warnedShape) { warnedShape = true; Plugin.Logger?.LogWarning("[STR] the map overlay has no drawer for shape " + p.Shape + "."); }
                    break;
            }
        }

        // A ring is a world polygon projected per vertex, so it stays true under the relief's tilt: solid draws every chord, dashed every second one. A ring
        // without a world radius (the highlight of a point-like item) is a fixed screen size.
        private void Ring(AvQuadBuffer b, in StrPrim p, IMapProjection projection, float factor, float cx, float cy, float px, Rgba c)
        {
            float w = (p.Selected ? 1.9f : 1.5f) * px;
            if (!(p.RadiusMetres > 0f)) { AvStrokes.Ring(b, cx, cy, 16f * px, RingSegments, w, c); return; }
            int n = RingSegments;
            for (int k = 0; k < n; k++)
            {
                float a = 6.2831853f * k / n;
                ringOk[k] = Project(projection, factor, p.X + p.RadiusMetres * Mathf.Cos(a), p.Z + p.RadiusMetres * Mathf.Sin(a), out ringX[k], out ringY[k]);
            }
            bool dashed = p.Shape == StrShape.DashedRing;
            for (int k = 0; k < n; k++)
            {
                if (dashed && (k & 1) == 1) continue;
                int j = (k + 1) % n;
                if (ringOk[k] && ringOk[j]) AvStrokes.Line(b, ringX[k], ringY[k], ringX[j], ringY[j], w, c);
            }
        }

        // Arrows and lines are dashed at a pitch that keeps the line inside its quad budget however far it runs.
        private static void Line(AvQuadBuffer b, StrShape shape, float x, float y, float x2, float y2, float phase, float px, Rgba c)
        {
            float len = Mathf.Sqrt((x2 - x) * (x2 - x) + (y2 - y) * (y2 - y)), cycle = Mathf.Max(13f * px, len / 36f), width = 1.6f * px;
            switch (shape)
            {
                case StrShape.Arrow: StrStrokes.Arrow(b, x, y, x2, y2, width, 9f * px, c); break;
                case StrShape.DashedArrow: StrStrokes.DashedArrow(b, x, y, x2, y2, cycle * 0.6f, cycle * 0.4f, phase, width, 9f * px, c); break;
                default: StrStrokes.DashedLine(b, x, y, x2, y2, cycle * 0.6f, cycle * 0.4f, phase, width, c); break;
            }
        }

        // A HOI4 battle plan: a fat arrow bowed to one side, its centre line projected point by point so it lies true on the relief's tilt.
        // A proposal nobody points at is hollow (outline, faint fill); the live operation is filled, and its chevrons march while it advances.
        private void Offensive(AvQuadBuffer b, in StrPrim p, IMapProjection projection, float factor, float px, Rgba c)
        {
            float dx = p.X2 - p.X, dz = p.Z2 - p.Z;
            if (!(dx * dx + dz * dz > 1f)) return;
            float bow = (p.Id.Key & 1) == 0 ? 0.08f : -0.08f;
            float cx = (p.X + p.X2) * 0.5f - dz * bow, cz = (p.Z + p.Z2) * 0.5f + dx * bow, run = 0f;
            int n = 0;
            for (int k = 0; k < StrStrokes.MaxCurve; k++)
            {
                float t = k / (StrStrokes.MaxCurve - 1f), u = 1f - t;
                float wx = u * u * p.X + 2f * u * t * cx + t * t * p.X2, wz = u * u * p.Z + 2f * u * t * cz + t * t * p.Z2;
                if (!Project(projection, factor, wx, wz, out curveX[n], out curveY[n])) continue;
                if (n > 0) run += Mathf.Sqrt((curveX[n] - curveX[n - 1]) * (curveX[n] - curveX[n - 1]) + (curveY[n] - curveY[n - 1]) * (curveY[n] - curveY[n - 1]));
                n++;
            }
            if (n < 2) return;
            // HOI4 look: a slim ribbon of near-constant width in solid ink, a dark rim to lift it off the relief, a modest head.
            float neck = Mathf.Clamp(run * 0.012f, 2.2f * px, 3.4f * px), tail = neck * 0.75f, head = neck * 2.6f;
            float fillA = p.Hollow ? 0.45f : 0.92f;
            var fill = new Rgba(c.R, c.G, c.B, c.A * fillA);
            var line = new Rgba(0f, 0f, 0f, c.A * (p.Hollow ? 0.3f : 0.55f));
            StrStrokes.Offensive(b, curveX, curveY, n, tail, neck, head, 0.9f * px, fill, line);
            if (!p.Pulse || pulseCount >= MaxPulse) return;
            System.Array.Copy(curveX, pulseCurveX[pulseCount], n);
            System.Array.Copy(curveY, pulseCurveY[pulseCount], n);
            pulseN[pulseCount] = n;
            pulseSize[pulseCount++] = neck * 0.8f;
        }

        // The marching offensive: chevrons slide toward the fix inside the arrow, and stand still under reduced motion.
        private void DrawPulse(float now)
        {
            AvQuadBuffer pb = pulse.Buffer;
            pb.Clear();
            var chevron = new Rgba(1f, 1f, 1f, 0.5f);
            for (int i = 0; i < pulseCount; i++)
            {
                float phase = AvFxDriver.ReducedMotion ? 0.5f : now * 0.35f;
                StrStrokes.Chevrons(pb, pulseCurveX[i], pulseCurveY[i], pulseN[i], phase, pulseSize[i], 1.1f * pulsePx, chevron);
            }
            DrawDraft(pb);
        }

        // ---- where an offensive jumps off ------------------------------------------------------------------------------------

        private static TacticalSectorGrid OwnGrid(DynamicMap map)
        {
            FactionHQ hq = map != null ? map.HQ : null;
            return hq != null && ModuleServices.TryGet(out ITerritoryIngress t) && t is TerritoryControlView view ? view.Read(hq) : null;
        }

        // Own ground vehicles on the map, where the garrisons stand; read once per plan.
        private int OwnGround(DynamicMap map)
        {
            int n = 0;
            FactionHQ hq = map.HQ;
            var icons = map.mapIcons;
            if (hq == null || icons == null) return 0;
            for (int i = 0; i < icons.Count && n < MaxGarrisonUnits; i++)
            {
                if (!(icons[i] is UnitMapIcon icon) || !(icon.unit is GroundVehicle unit) || unit.disabled || unit.NetworkHQ != hq) continue;
                GlobalPosition at = unit.GlobalPosition();
                if (!float.IsFinite(at.x) || !float.IsFinite(at.z)) continue;
                garrisonX[n] = at.x; garrisonZ[n] = at.z; n++;
            }
            return n;
        }

        // The garrison the push comes from; else a start on own ground behind the line from the sector field.
        private bool FindStart(float fx, float fz, out float sx, out float sz)
        {
            System.Func<float, float, int> side = startGrid != null ? sideOf ?? (sideOf = SideOf) : null;
            if (StrArrowStart.Garrison(garrisonX, garrisonZ, garrisonCount, side, fx, fz, out sx, out sz)) return true;
            TacticalSectorGrid g = startGrid;
            return g != null && StrArrowStart.Find(side, fx, fz, Mathf.Max(250f, g.CellSize), out sx, out sz);
        }

        private int SideOf(float x, float z)
        {
            TacticalSectorGrid g = startGrid;
            if (g == null || !g.WorldToCell(x, z, out int col, out int row)) return 0;
            SectorControl s = g.GetSectorControl(col, row);
            return s == SectorControl.Friendly ? 1 : s == SectorControl.Hostile ? -1 : 0;
        }

        // ---- the card -------------------------------------------------------------------------------------------------------

        private void UpdateCard(DynamicMap map, bool rebind)
        {
            cardDirty = false;
            if (!CardPlan.Valid) { card.Hide(); return; }
            StrMapTag tag = TagFor(CardPlan.Id);
            if (tag == null) { card.Hide(); return; }
            RectTransform viewport = map.mapBackground.rectTransform;
            tag.Root.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = viewport.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, local); max = Vector2.Max(max, local);
            }
            card.Show(Rect.MinMaxRect(min.x, min.y, max.x, max.y), viewport.rect, chrome.BottomReserve, rebind);
        }

        private StrMapTag TagFor(StrItemId id)
        {
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] != null && tags[i].gameObject.activeInHierarchy && tags[i].Id == id) return tags[i];
            return null;
        }

        // ---- geometry helpers -----------------------------------------------------------------------------------------------

        private Camera CameraOf(DynamicMap map)
        {
            Canvas canvas = map.mapImage.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;   // a nested canvas has no render mode of its own
            return MapUiPointer.UiCamera(canvas);
        }

        private static readonly Vector3[] screenCorners = new Vector3[4];

        internal static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            rect.GetWorldCorners(screenCorners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, screenCorners[0]), max = min;
            for (int i = 1; i < 4; i++)
            {
                Vector2 at = RectTransformUtility.WorldToScreenPoint(camera, screenCorners[i]);
                min = Vector2.Min(min, at); max = Vector2.Max(max, at);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // ---- read-back for the sim hooks and the harness --------------------------------------------------------------------

        /// <summary>How many tags of a kind are shown now.</summary>
        internal int TagCount(StrTagKind kind)
        {
            int n = 0;
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] != null && tags[i].gameObject.activeInHierarchy && tags[i].Kind == kind) n++;
            return n;
        }

        /// <summary>Screen position of a tag's centre, for Probe and the harness. False when the item has no tag on screen.</summary>
        internal bool TryGetTagScreenPoint(StrItemId id, out Vector2 screenPoint)
        {
            screenPoint = default;
            StrMapTag tag = TagFor(id);
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (tag == null || map == null || map.mapImage == null) return false;
            screenPoint = RectTransformUtility.WorldToScreenPoint(CameraOf(map), tag.Root.position);
            return true;
        }
        internal StrMapCard CardView => card;
    }
}
