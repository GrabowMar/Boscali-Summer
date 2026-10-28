using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>The MAP bezel's INDUSTRY and SUPPLY ROUTES layers on the expanded map. INDUSTRY marks every
    /// factory (square), depot (diamond) and ammo dump (triangle) the local faction knows of, in side colours.
    /// SUPPLY ROUTES runs a dashed line from each friendly one to the nearest point of the front and puts a
    /// chevron on every known supply truck. Enemy positions are faction-known fixes only. One graphic, rebuilt
    /// at 1 Hz or when the view moves; nothing runs while the map is closed.</summary>
    internal sealed class LogisticsMapOverlay : MonoBehaviour, ISceneService
    {
        private const int MaxSites = 256, MaxTrucks = 128;
        private const float RefreshInterval = 1f;

        internal enum SiteKind { Factory, Depot, Ammo }

        private struct Site
        {
            public Vector3 Position;
            public SiteKind Kind;
            public bool Friendly;
        }

        private struct Truck
        {
            public Vector3 Position;
            public Vector2 Heading;
            public bool Friendly;
        }

        private CommandSettings settings;
        private DynamicMap dynamicMap;
        private GameObject root;
        private RectTransform rootRect;
        private LogisticsGraphic graphic;
        private bool initialized;
        private float nextRefresh;

        private readonly Site[] sites = new Site[MaxSites];
        private readonly Truck[] trucks = new Truck[MaxTrucks];
        private readonly Vector2[] routeEnds = new Vector2[MaxSites];
        private readonly bool[] routed = new bool[MaxSites];
        private int siteCount, truckCount, routeCount;

        private readonly FrontlineTracePoint[] front = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] frontLengths = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly float[] frontPressures = new float[FrontlineTraceLimits.MaximumTraces];

        public bool Available => initialized && dynamicMap != null;
        public bool IndustryVisible => settings != null && settings.MapIndustry.Value;
        public bool SupplyVisible => settings != null && settings.MapSupplyRoutes.Value;
        /// <summary>Known factories, depots and ammo dumps in the last pass, both sides.</summary>
        public int SiteCount => siteCount;
        /// <summary>Known supply trucks in the last pass, both sides.</summary>
        public int TruckCount => truckCount;
        /// <summary>Friendly sites given a line to the front in the last pass; 0 without a front.</summary>
        public int RouteCount => routeCount;

        public void SetIndustryVisible(bool visible)
        {
            if (settings == null || settings.MapIndustry.Value == visible) return;
            settings.MapIndustry.Value = visible;
            nextRefresh = 0f;
        }

        public void SetSupplyVisible(bool visible)
        {
            if (settings == null || settings.MapSupplyRoutes.Value == visible) return;
            settings.MapSupplyRoutes.Value = visible;
            nextRefresh = 0f;
        }

        public void Configure(CommandSettings config) => settings = config;

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            rootRect = null;
            graphic = null;
            dynamicMap = null;
            initialized = false;
            siteCount = truckCount = routeCount = 0;
            nextRefresh = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value)
            {
                SetShown(false);
                return;
            }
            if (!initialized && DynamicMap.mapMaximized) TryInitialize();
            if (!initialized || dynamicMap == null || dynamicMap.mapImage == null)
            {
                SetShown(false);
                return;
            }

            bool shown = DynamicMap.mapMaximized && (IndustryVisible || SupplyVisible);
            SetShown(shown);
            if (!shown) return;
            if (rootRect.parent != dynamicMap.mapImage.transform) MapUiAccess.AttachInk(rootRect, dynamicMap);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;
            root = new GameObject("BoscaliLogistics", typeof(RectTransform), typeof(LogisticsGraphic));
            rootRect = (RectTransform)root.transform;
            MapUiAccess.AttachInk(rootRect, dynamicMap);
            graphic = root.GetComponent<LogisticsGraphic>();
            graphic.raycastTarget = false;
            initialized = true;
        }

        private void SetShown(bool visible)
        {
            if (root == null || root.activeSelf == visible) return;
            root.SetActive(visible);
        }

        // --------------------------------------------------------------- data pass

        private void Refresh()
        {
            siteCount = truckCount = routeCount = 0;
            FactionHQ hq = dynamicMap.HQ;
            var icons = dynamicMap.mapIcons;
            if (icons != null)
                for (int i = 0; i < icons.Count; i++)
                {
                    if (!(icons[i] is UnitMapIcon icon) || icon.unit == null || icon.unit.disabled) continue;
                    Unit unit = icon.unit;
                    bool friendly = hq == null || unit.NetworkHQ == hq;
                    if (unit is Building site && unit.definition is BuildingDefinition building &&
                        TryKind(building.buildingType, out SiteKind kind))
                    {
                        // A base's factory or dump works for whoever holds the base (its own HQ can lag a capture); one with no side at all is nobody's.
                        Airbase airbase = site.GetAirbase();
                        FactionHQ owner = airbase != null && airbase.CurrentHQ != null ? airbase.CurrentHQ : unit.NetworkHQ;
                        if (owner != null && siteCount < MaxSites && MapUiAccess.TryPosition(unit, hq, out Vector3 at, owner))
                            sites[siteCount++] = new Site { Position = at, Kind = kind, Friendly = hq == null || owner == hq };
                    }
                    else if (unit is GroundVehicle && unit.definition is VehicleDefinition vehicle &&
                        vehicle.vehicleType == VehicleType.TRUCK && truckCount < MaxTrucks &&
                        MapUiAccess.TryPosition(unit, hq, out Vector3 truckAt))
                    {
                        // Only our own trucks show which way they drive; a known enemy fix carries no heading.
                        Vector3 f = unit.transform.forward;
                        Vector2 heading = friendly ? new Vector2(f.x, f.z) : Vector2.zero;
                        trucks[truckCount++] = new Truck
                            { Position = truckAt, Heading = heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector2.zero, Friendly = friendly };
                    }
                }

            RouteToFront();
            if (graphic != null)
                graphic.SetSource(this, dynamicMap, IndustryVisible, SupplyVisible,
                    settings != null ? settings.OverlayOpacity.Value : .35f);
        }

        internal static bool TryKind(BuildingType type, out SiteKind kind)
        {
            switch (type)
            {
                case BuildingType.FAC: kind = SiteKind.Factory; return true;
                case BuildingType.DEP: kind = SiteKind.Depot; return true;
                case BuildingType.AMMO: kind = SiteKind.Ammo; return true;
                default: kind = default; return false;
            }
        }

        /// <summary>Nearest front point for every friendly site; the front is the control field's own trace.</summary>
        private void RouteToFront()
        {
            for (int i = 0; i < siteCount; i++) routed[i] = false;
            if (!ModuleServices.TryGet(out ComMapOverlay control) || control.Grid == null) return;
            int traces = control.Grid.CopyFrontlineTraces(front, frontLengths, frontPressures);
            int points = 0;
            for (int t = 0; t < traces; t++) points += frontLengths[t];
            if (points <= 0) return;
            // ponytail: sites x front points brute force (256 x ~4k at 1 Hz); a grid bucket if fronts get longer.
            for (int i = 0; i < siteCount; i++)
            {
                if (!sites[i].Friendly) continue;
                float bestSq = float.MaxValue;
                Vector2 best = default;
                for (int p = 0; p < points; p++)
                {
                    float dx = front[p].X - sites[i].Position.x, dz = front[p].Z - sites[i].Position.z;
                    float d = dx * dx + dz * dz;
                    if (d < bestSq) { bestSq = d; best = new Vector2(front[p].X, front[p].Z); }
                }
                routeEnds[i] = best;
                routed[i] = true;
                routeCount++;
            }
        }

        // ------------------------------------------------------------------ graphic

        [RequireComponent(typeof(CanvasRenderer))]
        [DefaultExecutionOrder(101)] // After the relief commits its pose.
        private sealed class LogisticsGraphic : MaskableGraphic
        {
            private LogisticsMapOverlay owner;
            private DynamicMap map;
            private bool industry, supply, projected;
            private float opacity, lastScale, lastFactor, lastSize;
            private int revision = -1;

            internal void SetSource(LogisticsMapOverlay source, DynamicMap dynamicMap, bool showIndustry, bool showSupply, float alpha)
            {
                owner = source; map = dynamicMap; industry = showIndustry; supply = showSupply;
                opacity = Mathf.Clamp01(alpha);
                SetVerticesDirty();
            }

            private void LateUpdate()
            {
                bool active = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
                int view = active ? projection.Revision : 0;
                float scale = transform.lossyScale.x, factor = map != null ? map.mapDisplayFactor : 0f;
                float size = MapSymbology.SizeFactor();
                if (active == projected && view == revision && scale == lastScale && factor == lastFactor && size == lastSize) return;
                projected = active; revision = view; lastScale = scale; lastFactor = factor; lastSize = size;
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (owner == null || map == null || !isActiveAndEnabled || !(map.mapDisplayFactor > 0f)) return;
                float px = 1f / Mathf.Max(.001f, Mathf.Abs(transform.lossyScale.x));
                bool active = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
                float ink = Mathf.Clamp01(opacity * 2.4f);
                Color friendly = AvTheme.RailInfo.WithAlpha(ink), hostile = AvTheme.RailDanger.WithAlpha(ink);
                // The SYMBOL SCALE choice sizes these marks like the contact glyphs.
                float r = 7f * MapSymbology.SizeFactor() * px;

                if (supply)
                {
                    for (int i = 0; i < owner.siteCount; i++)
                    {
                        if (!owner.routed[i]) continue;
                        Vector3 s = owner.sites[i].Position;
                        Vector2 e = owner.routeEnds[i];
                        if (Project(s.x, s.z, projection, active, out Vector2 a) && Project(e.x, e.y, projection, active, out Vector2 b))
                            Dashed(vh, a, b, 6f * px, 4f * px, .55f * px, px, friendly.WithAlpha(ink * .8f));
                    }
                    for (int i = 0; i < owner.truckCount; i++)
                    {
                        Truck t = owner.trucks[i];
                        if (!Project(t.Position.x, t.Position.z, projection, active, out Vector2 at)) continue;
                        Color c = t.Friendly ? friendly : hostile;
                        if (t.Heading == Vector2.zero) { Diamond(vh, at, r * .55f, px, c); continue; }
                        // A projected step ahead turns the world heading into a screen heading on the relief too.
                        Vector2 dir = Project(t.Position.x + t.Heading.x * 50f, t.Position.z + t.Heading.y * 50f,
                            projection, active, out Vector2 ahead) && (ahead - at).sqrMagnitude > 1e-8f
                            ? (ahead - at).normalized : t.Heading;
                        Vector2 side = new Vector2(-dir.y, dir.x);
                        Vector2 tip = at + dir * r * 1.1f;
                        MapStroke.Segment(vh, tip, tip - dir * r + side * r * .7f, .7f * px, px, c);
                        MapStroke.Segment(vh, tip, tip - dir * r - side * r * .7f, .7f * px, px, c);
                    }
                }

                if (industry)
                    for (int i = 0; i < owner.siteCount; i++)
                    {
                        Site s = owner.sites[i];
                        if (!Project(s.Position.x, s.Position.z, projection, active, out Vector2 at)) continue;
                        Color c = s.Friendly ? friendly : hostile;
                        if (s.Kind == SiteKind.Factory) Square(vh, at, r, px, c);
                        else if (s.Kind == SiteKind.Depot) Diamond(vh, at, r * 1.25f, px, c);
                        else Triangle(vh, at, r * 1.2f, px, c);
                    }
            }

            private static void Square(VertexHelper vh, Vector2 c, float r, float px, Color ink)
            {
                Vector2 a = c + new Vector2(-r, -r), b = c + new Vector2(r, -r), d = c + new Vector2(r, r), e = c + new Vector2(-r, r);
                Loop(vh, px, ink, a, b, d, e);
            }

            private static void Diamond(VertexHelper vh, Vector2 c, float r, float px, Color ink) =>
                Loop(vh, px, ink, c + Vector2.left * r, c + Vector2.up * r, c + Vector2.right * r, c + Vector2.down * r);

            private static void Triangle(VertexHelper vh, Vector2 c, float r, float px, Color ink) =>
                Loop(vh, px, ink, c + Vector2.up * r, c + new Vector2(.87f * r, -.5f * r), c + new Vector2(-.87f * r, -.5f * r));

            private static void Loop(VertexHelper vh, float px, Color ink, params Vector2[] corners)
            {
                for (int i = 0; i < corners.Length; i++)
                    MapStroke.Segment(vh, corners[i], corners[(i + 1) % corners.Length], .8f * px, px, ink);
            }

            private static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float dash, float gap, float half, float px, Color ink)
            {
                float length = Vector2.Distance(a, b);
                if (!(length > 0f)) return;
                Vector2 dir = (b - a) / length;
                // Bounded: a cross-theatre line at max zoom still stops at 200 dashes.
                float step = Mathf.Max(dash + gap, length / 200f);
                for (float t = 0f; t < length; t += step)
                    MapStroke.Segment(vh, a + dir * t, a + dir * Mathf.Min(length, t + dash), half, px, ink);
            }

            private bool Project(float x, float z, IMapProjection projection, bool active, out Vector2 point)
            {
                if (active)
                {
                    bool ok = projection.TryProject(x, z, out float mx, out float my);
                    point = new Vector2(mx, my);
                    return ok;
                }
                point = new Vector2(x * map.mapDisplayFactor, z * map.mapDisplayFactor);
                return float.IsFinite(point.x) && float.IsFinite(point.y);
            }
        }
    }
}
