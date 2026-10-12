using System.Diagnostics;
using BoscaliSummer.Core.Contracts;
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
    internal sealed class StrMapOverlay : MonoBehaviour, ISceneService
    {
        internal const int InkCapacity = 900, PulseCapacity = 120;
        private const float PoseSeconds = 0.066f, PulseSeconds = 1f / 15f, WindowSeconds = 5f, LeaderClear = 14f, ViewMargin = 6f;
        private const int RingSegments = 48;

        /// <summary>The scene's overlay, for the automation hooks and the harness.</summary>
        internal static StrMapOverlay Active { get; private set; }

        /// <summary>Test seam: the harness supplies its own hub (the real one needs a live scene).</summary>
        internal IStrHub HubOverride;
        internal IStrHub Hub => HubOverride ?? (IStrHub)StrViewHub.Active;

        internal StrOverlayPlan Plan { get; } = new StrOverlayPlan();
        internal StrCardPlan CardPlan { get; } = new StrCardPlan();
        /// <summary>The layer toggles and the card's buttons by control id (layer.*, map.card.*).</summary>
        internal StrControls Controls { get; } = new StrControls();

        /// <summary>Counters for the sim read-back and the harness: rebuilds, quads written, overflow, the last rebuild's cost and the cost per second over a 5 s window.</summary>
        internal int Rebuilds, InkQuads, PulseQuads;
        internal bool Overflowed, Visible, Relief;
        internal float LastRebuildMs, MsPerSecond, RebuildsPerSecond;

        private readonly RebuildGate gate = new RebuildGate(PoseSeconds);
        private readonly System.Collections.Generic.List<StrMapTag> tags = new System.Collections.Generic.List<StrMapTag>(StrOverlayPlan.MaxTags);
        private readonly float[] ax = new float[StrOverlayPlan.MaxTags], ay = new float[StrOverlayPlan.MaxTags], tw = new float[StrOverlayPlan.MaxTags], th = new float[StrOverlayPlan.MaxTags];
        private readonly RectF[] placed = new RectF[StrOverlayPlan.MaxTags];
        private readonly Vector2[] anchorLocal = new Vector2[StrOverlayPlan.MaxTags];
        private readonly float[] ringX = new float[RingSegments], ringY = new float[RingSegments];
        private readonly bool[] ringOk = new bool[RingSegments];
        private readonly Vector3[] corners = new Vector3[4];
        // The marching axis, kept projected so the pulse mesh can redraw at 15 Hz without projecting again.
        private const int MaxPulse = 2;
        private readonly float[] pulseX = new float[MaxPulse], pulseY = new float[MaxPulse], pulseX2 = new float[MaxPulse], pulseY2 = new float[MaxPulse];
        private readonly StrInkRole[] pulseInk = new StrInkRole[MaxPulse];
        private int pulseCount;
        private float pulsePx = 1f, nextPulse;

        private RectTransform root, tagLayer;
        private AvVector ink, pulse;
        private StrMapChrome chrome;
        private StrMapCard card;
        private StrViewModel planModel;
        private int planRevision = -1;
        private StrLayers planLayers = StrLayers.None;
        private StrItemId planSelected, planHover;
        private bool cardDirty, warnedShape;
        private float windowStart = -1f, windowMs;
        private int windowRebuilds;

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            Teardown();
        }

        public void ResetForScene() => Teardown();

        private void LateUpdate()
        {
            Tick(Time.unscaledTime);
            if (Visible && !Application.isBatchMode && Input.GetKeyDown(KeyCode.Escape))
            {
                IStrHub hub = Hub;
                if (hub != null && !hub.Ui.Selected.IsNone) hub.ClearSelection();
            }
        }

        /// <summary>Brings the overlay up to date now: the sim presses and the harness call this so a press never meets a stale card.</summary>
        internal void RefreshNow() => Tick(Time.unscaledTime);

        /// <summary>One frame of the overlay. Cheap when nothing changed: it compares the model's revision, the layers and the map's pose and builds only on a difference.</summary>
        internal void Tick(float now)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            IStrHub hub = Hub;
            if (hub == null || map == null || map.mapImage == null || map.mapBackground == null || !DynamicMap.mapMaximized) { Hide(); return; }
            if (root == null && !Build(map)) return;

            StrViewModel vm = hub.Current;
            StrUiState ui = hub.Ui;
            StrLayers layers = (ui.ShowFronts ? StrLayers.Fronts : 0) | (ui.ShowOperation ? StrLayers.Operation : 0) | (ui.ShowSorties ? StrLayers.Sorties : 0);
            if (!Visible) { Visible = true; chrome.SetShown(true); planRevision = -1; gate.Reset(); }
            bool worldOn = layers != StrLayers.None;
            if (root.gameObject.activeSelf != worldOn) root.gameObject.SetActive(worldOn);

            bool planChanged = !ReferenceEquals(vm, planModel) || vm.Revision != planRevision || layers != planLayers || vm.SelectedId != planSelected || vm.HoverId != planHover;
            long t0 = Stopwatch.GetTimestamp();
            if (planChanged)
            {
                planModel = vm; planRevision = vm.Revision; planLayers = layers; planSelected = vm.SelectedId; planHover = vm.HoverId;
                StrOverlayPlan.Build(Plan, vm, layers);
                StrCardPlan.Build(CardPlan, vm, worldOn ? vm.SelectedId : StrItemId.None);
                cardDirty = true;
                chrome.BindPlate(vm, in ui);
            }
            IMapProjection projection = ModuleServices.TryGet(out IMapProjection p) && p.IsActive ? p : null;
            Relief = projection != null;
            if (planChanged) chrome.BindLegend(Plan, map.mapBackground.rectTransform.rect.width);

            bool poseChanged = gate.ShouldRebuild(Pose(map, projection), now);
            bool rebuilt = false;
            if (worldOn && (planChanged || poseChanged)) rebuilt = Rebuild(map, projection, now);
            else if (!worldOn) Plan.TagCount = 0;
            if (worldOn && pulseCount > 0 && !AvFxDriver.ReducedMotion && now >= nextPulse && !rebuilt)
            {
                nextPulse = now + PulseSeconds;
                DrawPulse(now);
                pulse.Commit();
                PulseQuads = pulse.Buffer.Count;
            }
            if (planChanged || rebuilt || cardDirty) UpdateCard(map, planChanged);
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
                h = h * 31 + Mathf.RoundToInt(at.x * 4f);
                h = h * 31 + Mathf.RoundToInt(at.y * 4f);
                return h * 31 + Mathf.RoundToInt(scale.x * 1000f);
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
            if (!Visible) return;
            Visible = false;
            if (root != null) root.gameObject.SetActive(false);
            chrome?.SetShown(false);
            card?.Hide();
        }

        private static void Remove(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private void Teardown()
        {
            if (root != null) Remove(root.gameObject);
            if (chrome != null) { MapUiPointer.Unregister(chrome.Plate); MapUiPointer.Unregister(chrome.Legend); Remove(chrome.Plate.gameObject); Remove(chrome.Legend.gameObject); }
            if (card != null) { MapUiPointer.Unregister(card.Root); Remove(card.Root.gameObject); }
            root = tagLayer = null; ink = pulse = null; chrome = null; card = null;
            tags.Clear();
            Controls.Clear();
            Plan.PrimCount = Plan.TagCount = Plan.LegendCount = 0;
            CardPlan.Valid = false;
            planModel = null; planRevision = -1; planLayers = StrLayers.None; planSelected = planHover = StrItemId.None;
            pulseCount = 0; cardDirty = false; warnedShape = false;
            Visible = false; Relief = false;
            gate.Reset();
            windowStart = -1f; windowMs = 0f; windowRebuilds = 0;
        }

        // ---- the rebuild ----------------------------------------------------------------------------------------------------

        private bool Rebuild(DynamicMap map, IMapProjection projection, float now)
        {
            float factor = map.mapDisplayFactor, zoom = Mathf.Abs(map.mapImage.transform.localScale.x);
            if (!(zoom > 1e-4f) || (projection == null && !(factor > 1e-6f))) return false;
            float px = 1f / zoom;
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
            for (int i = 0; i < n; i++)
            {
                StrTagSpec s = Plan.Tags[i];
                StrMapTag tag = TagAt(i);
                bool ok = Project(projection, factor, s.X, s.Z, out float lx, out float ly);
                anchorLocal[i] = new Vector2(lx, ly);
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(camera, image.TransformPoint(new Vector3(lx, ly, 0f)));
                bool inside = ok && sp.x >= screen.xMin + ViewMargin && sp.x <= screen.xMax - ViewMargin && sp.y >= screen.yMin + ViewMargin && sp.y <= screen.yMax - ViewMargin;
                tag.Show(in s, px);
                tag.AnchorLocal = anchorLocal[i];
                Rect size = tag.ScreenBounds(camera);
                ax[i] = inside ? sp.x : float.NaN; ay[i] = inside ? sp.y : float.NaN; tw[i] = size.width; th[i] = size.height;
            }
            StrTagPlacer.Place(in view, ax, ay, tw, th, n, placed);
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(ax[i])) { tags[i].Hide(); continue; }
                tags[i].PlaceAt(new Vector2(placed[i].X + placed[i].W * 0.5f, placed[i].Y + placed[i].H * 0.5f), camera);
            }
            for (int i = n; i < tags.Count; i++) tags[i].Hide();

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
            x = y = 0f;
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
                    if (p.Pulse && pulseCount < MaxPulse)
                    {
                        pulseX[pulseCount] = x; pulseY[pulseCount] = y; pulseX2[pulseCount] = x2; pulseY2[pulseCount] = y2; pulseInk[pulseCount++] = p.Ink;
                        return;
                    }
                    Line(b, p.Shape, x, y, x2, y2, 0f, px, c);
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

        // The marching axis: the dashes slide toward the fix, and stand still under reduced motion.
        private void DrawPulse(float now)
        {
            AvQuadBuffer pb = pulse.Buffer;
            pb.Clear();
            for (int i = 0; i < pulseCount; i++)
            {
                float phase = AvFxDriver.ReducedMotion ? 0f : -now * 30f * pulsePx;
                Line(pb, StrShape.DashedArrow, pulseX[i], pulseY[i], pulseX2[i], pulseY2[i], phase, pulsePx, StrInk.RgbaOf(pulseInk[i]));
            }
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
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }

        private Rect ScreenRect(RectTransform rect, Camera camera)
        {
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
            for (int i = 1; i < 4; i++)
            {
                Vector2 at = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
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

        /// <summary>The tag shown for an item, or null.</summary>
        internal StrMapTag Tag(StrItemId id) => TagFor(id);

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

        /// <summary>The ink mesh's own quads, for the harness gate: the whole buffer, its capacity and whether it overflowed.</summary>
        internal AvQuadBuffer InkBuffer => ink != null ? ink.Buffer : null;
        internal AvQuadBuffer PulseBuffer => pulse != null ? pulse.Buffer : null;
        internal bool WarnedShape => warnedShape;
        internal StrMapCard CardView => card;
        internal StrMapChrome ChromeView => chrome;
        internal int ActiveTags => tags.FindAll(t => t != null && t.gameObject.activeInHierarchy).Count;
        internal System.Collections.Generic.IReadOnlyList<StrMapTag> TagPool => tags;
    }
}
