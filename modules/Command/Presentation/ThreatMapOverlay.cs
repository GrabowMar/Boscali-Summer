using HarmonyLib;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>Range rings of the hovered map unit, drawn on the relief. The stock tooltip draws its
    /// weapon and sensor circles on the flat sheet, which the relief pins and hides, so they land in the
    /// wrong place there: while the relief draws, those circles are switched off and the same two ranges
    /// are drawn as projected vector rings instead (solid: sensor reach, dashed: weapon reach).</summary>
    internal sealed class ThreatMapOverlay : MonoBehaviour, ISceneService
    {
        private const int MaxEmitters = 1;
        private const float MinimumDrawableRadius = 1f;

        /// <summary>One unit's two ranges, kept from the range pass to the bake.</summary>
        private struct Sample
        {
            public Vector3 Position;
            public float RadarRadius;
            public float OpticalRadius;
        }

        private static readonly AccessTools.FieldRef<DynamicMap, MapToolTip> ToolTipRef =
            AccessTools.FieldRefAccess<DynamicMap, MapToolTip>("toolTip");
        private static readonly AccessTools.FieldRef<MapToolTip, MapIcon> HoverRef =
            AccessTools.FieldRefAccess<MapToolTip, MapIcon>("icon");
        private static readonly AccessTools.FieldRef<MapToolTip, Image> Circle1Ref =
            AccessTools.FieldRefAccess<MapToolTip, Image>("circle1");
        private static readonly AccessTools.FieldRef<MapToolTip, Image> Circle2Ref =
            AccessTools.FieldRefAccess<MapToolTip, Image>("circle2");

        private CommandSettings settings;

        private DynamicMap dynamicMap;
        private GameObject root;
        private RectTransform rootRect;
        private ThreatEnvelopeGraphic graphic;
        private readonly Sample[] samples = new Sample[MaxEmitters];
        private bool initialized;

        /// <summary>True once the map is on screen and the overlay can be drawn at all.</summary>
        public bool Available => initialized && dynamicMap != null;

        /// <summary>The hovered unit's sensor reach in kilometres; 0 when nothing with a sensor is hovered.</summary>
        public float HoverSensorKm { get; private set; }

        public bool Visible => settings != null && settings.ThreatHeat.Value;

        public void SetVisible(bool visible)
        {
            if (settings == null || settings.ThreatHeat.Value == visible) return;
            settings.ThreatHeat.Value = visible;
        }

        public void Configure(CommandSettings config)
        {
            settings = config;
        }

        public void ResetForScene()
        {
            if (root != null)
            {
                Destroy(root);
                root = null;
            }

            graphic = null;
            rootRect = null;
            dynamicMap = null;
            HoverSensorKm = 0f;
            initialized = false;
            TheaterFrame.Invalidate();
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

            // The flat map's own circles are right; only the relief needs the replacement.
            bool relief = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
            bool shown = settings.ThreatHeat.Value && DynamicMap.mapMaximized && relief;
            SetShown(shown);
            if (!shown) return;

            // The map regenerates its image on a scene change; stay on it when it does.
            if (rootRect.parent != dynamicMap.mapImage.transform) Reparent();
            Refresh();
        }

        /// <summary>The stock tooltip re-scales and re-enables its range circles in its own refresh
        /// (they are the ones sized to one unit before scaling); switch them off after it.</summary>
        private void LateUpdate()
        {
            if (root == null || !root.activeSelf) return;
            MapToolTip tip = ToolTipRef(dynamicMap);
            if (tip == null) return;
            HideRangeCircle(Circle1Ref(tip));
            HideRangeCircle(Circle2Ref(tip));
        }

        private static void HideRangeCircle(Image circle)
        {
            // Airbase and factory progress dials reuse these images at 50-70 px; leave those alone.
            if (circle != null && circle.enabled && circle.rectTransform.sizeDelta == Vector2.one) circle.enabled = false;
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            root = new GameObject("BoscaliHoverRanges", typeof(RectTransform), typeof(ThreatEnvelopeGraphic));
            rootRect = (RectTransform)root.transform;
            Reparent();

            graphic = root.GetComponent<ThreatEnvelopeGraphic>();
            graphic.raycastTarget = false;

            initialized = true;
        }

        private void Reparent()
        {
            rootRect.SetParent(dynamicMap.mapImage.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.localScale = Vector3.one;
            rootRect.localPosition = Vector3.zero;
            // Vector ink sits above the territory tint and below the map's native contacts.
            rootRect.SetSiblingIndex(dynamicMap.mapImage.transform.childCount > 1 ? 1 : 0);
        }

        private void SetShown(bool visible)
        {
            if (!visible) HoverSensorKm = 0f;
            if (root == null || root.activeSelf == visible) return;
            root.SetActive(visible);
        }

        // --------------------------------------------------------------- range pass

        private void Refresh()
        {
            int used = 0;
            HoverSensorKm = 0f;
            MapToolTip tip = ToolTipRef(dynamicMap);
            Unit unit = tip != null && tip.isActiveAndEnabled && HoverRef(tip) is UnitMapIcon icon ? icon.unit : null;
            // The stock tooltip shows an aircraft's rings to its own side only, and none for a missile.
            FactionHQ hq = dynamicMap.HQ;
            if (unit != null && !unit.disabled && !(unit is Missile) &&
                (!(unit is Aircraft) || hq == null || unit.NetworkHQ == hq) && TryPosition(unit, out Vector3 position))
            {
                float sensor = SensorRange(unit), weapon = WeaponRange(unit);
                if (sensor >= MinimumDrawableRadius || weapon >= MinimumDrawableRadius)
                {
                    samples[0] = new Sample { Position = position, RadarRadius = sensor, OpticalRadius = weapon };
                    used = 1;
                    HoverSensorKm = sensor * 0.001f;
                }
            }
            if (graphic != null)
                graphic.SetSource(samples, used, dynamicMap, settings != null ? settings.OverlayOpacity.Value : .65f);
        }

        /// <summary>Own side: the live position. Others: only what the local faction knows.</summary>
        private bool TryPosition(Unit unit, out Vector3 position)
        {
            FactionHQ hq = dynamicMap.HQ;
            GlobalPosition? at = hq == null || unit.NetworkHQ == hq ? unit.GlobalPosition() : hq.GetKnownPosition(unit);
            position = at.HasValue ? at.Value.AsVector3() : default;
            return at.HasValue && float.IsFinite(position.x) && float.IsFinite(position.z);
        }

        /// <summary>The stock tooltip's sensor figure: the longest radar range, else the longest visual range.</summary>
        private static float SensorRange(Unit unit)
        {
            // ponytail: walks the hovered unit's hierarchy every frame; one unit, cache it if it ever shows in a profile.
            float best = 0f;
            foreach (TargetDetector detector in unit.GetComponentsInChildren<TargetDetector>())
            {
                if (detector is Radar && detector.GetRadarRange() > best) best = detector.GetRadarRange();
                else if (detector.GetVisualRange() > best) best = detector.GetVisualRange();
            }
            return float.IsFinite(best) ? best : 0f;
        }

        /// <summary>The stock tooltip's weapon figure: an aircraft's selected station, else the longest
        /// anti-air or anti-missile station.</summary>
        private static float WeaponRange(Unit unit)
        {
            if (unit is Aircraft aircraft)
            {
                WeaponInfo info = aircraft.weaponManager != null ? aircraft.weaponManager.currentWeaponStation?.WeaponInfo : null;
                return info != null ? info.targetRequirements.maxRange : 0f;
            }
            float best = 0f;
            foreach (WeaponStation station in unit.weaponStations)
            {
                WeaponInfo info = station?.WeaponInfo;
                if (info != null && (info.effectiveness.antiAir > 0f || info.effectiveness.antiMissile > 0f))
                    best = Mathf.Max(best, info.targetRequirements.maxRange);
            }
            return best;
        }

        [RequireComponent(typeof(CanvasRenderer))]
        [DefaultExecutionOrder(101)]
        private sealed class ThreatEnvelopeGraphic : MaskableGraphic
        {
            private readonly Vector2[] points = new Vector2[161];
            private readonly Sample[] sites = new Sample[MaxEmitters];
            private int count, revision = -1;
            private float opacity, lastScale, lastFactor;
            private DynamicMap map;
            private bool projected;
            private Color lastRadarInk, lastOpticalInk;

            internal void SetSource(Sample[] source, int length, DynamicMap owner, float alpha)
            {
                bool changed = count != length || map != owner || opacity != alpha;
                count = length; map = owner; opacity = Mathf.Clamp01(alpha);
                for (int i = 0; i < count; i++)
                {
                    changed |= sites[i].Position != source[i].Position ||
                        sites[i].RadarRadius != source[i].RadarRadius || sites[i].OpticalRadius != source[i].OpticalRadius;
                    sites[i] = source[i];
                }
                if (changed) SetVerticesDirty();
            }

            private void LateUpdate()
            {
                bool active = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
                int view = active ? projection.Revision : 0;
                float scale = transform.lossyScale.x;
                float factor = map != null ? map.mapDisplayFactor : 0f;
                Color radarInk = AvTheme.RailDanger, opticalInk = AvTheme.RailCaution;
                if (active == projected && view == revision && scale == lastScale && factor == lastFactor &&
                    radarInk == lastRadarInk && opticalInk == lastOpticalInk) return;
                projected = active; revision = view; lastScale = scale; lastFactor = factor;
                lastRadarInk = radarInk; lastOpticalInk = opticalInk;
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (map == null || !isActiveAndEnabled || !(map.mapDisplayFactor > 0f)) return;
                float scale = Mathf.Max(.001f, Mathf.Abs(transform.lossyScale.x));
                bool active = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
                Color radar = AvTheme.RailDanger.WithAlpha(Mathf.Clamp01(opacity * 1.8f));
                Color optical = AvTheme.RailCaution.WithAlpha(Mathf.Clamp01(opacity * 1.6f));
                for (int i = 0; i < count; i++)
                {
                    Sample site = sites[i];
                    Ring(vh, site.Position, site.RadarRadius, false, radar, projection, active, scale);
                    Ring(vh, site.Position, site.OpticalRadius, true, optical, projection, active, scale);
                    if (Project(site.Position.x, site.Position.z, projection, active, out Vector2 at))
                    {
                        float r = 4f / scale;
                        Color ink = site.RadarRadius > 0f ? radar : optical;
                        MapStroke.Segment(vh, at + Vector2.left*r, at + Vector2.up*r, .65f/scale, 1f/scale, ink);
                        MapStroke.Segment(vh, at + Vector2.up*r, at + Vector2.right*r, .65f/scale, 1f/scale, ink);
                        MapStroke.Segment(vh, at + Vector2.right*r, at + Vector2.down*r, .65f/scale, 1f/scale, ink);
                        MapStroke.Segment(vh, at + Vector2.down*r, at + Vector2.left*r, .65f/scale, 1f/scale, ink);
                    }
                }
            }

            private void Ring(VertexHelper vh, Vector3 centre, float radius, bool dashed, Color ink,
                IMapProjection projection, bool active, float scale)
            {
                if (!float.IsFinite(radius) || radius < MinimumDrawableRadius) return;
                // Screen-space chord error below a fraction of a pixel at normal map scales.
                // ponytail: 160 stations per envelope; bounded below 43000 vertices for 32 dual-sensor sites.
                int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(radius * map.mapDisplayFactor * scale) * 6f), 48, 160);
                int run = 0;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * (2f * Mathf.PI / segments);
                    bool visible = Project(centre.x + Mathf.Cos(angle)*radius, centre.z + Mathf.Sin(angle)*radius,
                        projection, active, out Vector2 point);
                    if (visible) points[run++] = point * scale;
                    if (!visible || i == segments || dashed && i % 4 == 3)
                    {
                        MapStroke.Ribbon(vh, points, 0, run, 1f/scale, .7f, 1f, ink);
                        run = 0;
                    }
                }
            }

            private bool Project(float x, float z, IMapProjection projection, bool active, out Vector2 point)
            {
                if (active)
                {
                    bool ok = projection.TryProject(x, z, out float px, out float py);
                    point = new Vector2(px, py); return ok;
                }
                point = new Vector2(x * map.mapDisplayFactor, z * map.mapDisplayFactor);
                return float.IsFinite(point.x) && float.IsFinite(point.y);
            }
        }
    }
}
