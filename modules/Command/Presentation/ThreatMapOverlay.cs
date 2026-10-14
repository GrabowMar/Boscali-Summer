using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
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
    /// <summary>Faction-known sensor envelopes on the native map. One bounded vector mesh,
    /// solid radar / dashed optical, using the same ground projection as native contacts.
    /// These are detection estimates, not missile ranges; terrain masking is not applied.</summary>
    internal sealed class ThreatMapOverlay : MonoBehaviour, ISceneService
    {
        private const int MaxEmitters = 32;
        private const int SensorCacheLimit = 512;
        private const float RefreshSeconds = 1f;
        private const float MinimumDrawableRadius = 1f;

        private sealed class Sensor
        {
            public TargetDetector[] Detectors;
            public Radar Radar;
            public TargetDetector Optical;

            public bool Any => Radar != null || Optical != null;
        }

        /// <summary>One emitter's contribution to the field, kept from the envelope pass to the bake.</summary>
        private struct Sample
        {
            public Vector3 Position;
            public float RadarRadius;
            public float OpticalRadius;
        }

        private readonly struct Contact
        {
            public Contact(Unit unit, Vector3 position, float distanceSquared)
            {
                Unit = unit;
                Position = position;
                DistanceSquared = distanceSquared;
            }

            public readonly Unit Unit;
            public readonly Vector3 Position;
            public readonly float DistanceSquared;
        }

        private static readonly Comparison<Contact> NearestFirst =
            (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared);

        private CommandSettings settings;

        private DynamicMap dynamicMap;
        private GameObject root;
        private RectTransform rootRect;
        private ThreatEnvelopeGraphic graphic;
        private readonly List<Contact> contacts = new List<Contact>(MaxEmitters * 2);
        private readonly Dictionary<int, Sensor> sensorCache = new Dictionary<int, Sensor>(MaxEmitters * 4);
        private readonly Sample[] samples = new Sample[MaxEmitters];

        private float nextRefresh;
        private bool initialized;

        /// <summary>True once the map is on screen and the overlay can be drawn at all.</summary>
        public bool Available => initialized && dynamicMap != null;

        /// <summary>How many known sites supplied a drawable detection envelope.</summary>
        public int TrackedEmitters { get; private set; }

        /// <summary>
        /// The widest estimated detection envelope in kilometres.
        /// </summary>
        public float WidestEnvelopeKm { get; private set; }

        public bool HasOwnship { get; private set; }
        public bool HasPicture { get; private set; }

        public bool Visible => settings != null && settings.ThreatHeat.Value;

        public void SetVisible(bool visible)
        {
            if (settings == null || settings.ThreatHeat.Value == visible) return;
            settings.ThreatHeat.Value = visible;
            nextRefresh = 0f;
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
            HasOwnship = false;
            HasPicture = false;

            contacts.Clear();
            sensorCache.Clear();
            rootRect = null;
            dynamicMap = null;
            TrackedEmitters = 0;
            WidestEnvelopeKm = 0f;
            nextRefresh = 0f;
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

            // The layer toggle hides the field; it must never gate readiness. Initialising only
            // while the layer is on makes switching it off a one-way door: the overlay reports
            // unavailable, and the panel cannot offer the click that would bring it back.
            if (!initialized && DynamicMap.mapMaximized) TryInitialize();

            if (!initialized || dynamicMap == null || dynamicMap.mapImage == null)
            {
                SetShown(false);
                return;
            }

            bool shown = settings.ThreatHeat.Value && DynamicMap.mapMaximized;
            bool wasShown = root.activeSelf;
            SetShown(shown);
            if (!shown) return;

            // The map regenerates its image on a scene change; stay on it when it does.
            if (rootRect.parent != dynamicMap.mapImage.transform) Reparent();

            // Coming back on screen bakes on the first frame, not on the next tick.
            if (!wasShown) nextRefresh = 0f;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh();
            }
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            root = new GameObject("BoscaliThreatEnvelopes", typeof(RectTransform), typeof(ThreatEnvelopeGraphic));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(dynamicMap.mapImage.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.localScale = Vector3.one;
            rootRect.localPosition = Vector3.zero;

            graphic = root.GetComponent<ThreatEnvelopeGraphic>();
            graphic.raycastTarget = false;

            // Vector ink sits above the territory tint and below the map's native contacts.
            root.transform.SetSiblingIndex(dynamicMap.mapImage.transform.childCount > 1 ? 1 : 0);

            SetShown(true);
            initialized = true;
            Plugin.Logger?.LogInfo("[COM] Vector sensor envelopes initialized (32 sites maximum).");
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
            rootRect.SetSiblingIndex(dynamicMap.mapImage.transform.childCount > 1 ? 1 : 0);
        }

        private void SetShown(bool visible)
        {
            if (root == null || root.activeSelf == visible) return;
            root.SetActive(visible);
        }

        // --------------------------------------------------------------- envelope pass

        private void Refresh()
        {
            FactionHQ hq = dynamicMap.HQ;
            Aircraft ownship = ResolveOwnship();
            HasOwnship = ownship != null;
            HasPicture = hq != null;
            if (hq == null)
            {
                TrackedEmitters = 0;
                WidestEnvelopeKm = 0f;
                Bake();
                return;
            }

            CollectContacts(hq, ownship);
            if (contacts.Count > 1) contacts.Sort(NearestFirst);

            Vector3 ownPosition = ownship != null ? ownship.GlobalPosition().AsVector3() : Vector3.zero;
            float ownRcs = ownship != null ? ownship.RCS : 0f;
            float ownVisibility = ownship != null ? ownship.GetVisibility() : 0f;

            int used = 0;
            float widest = 0f;
            for (int i = 0; i < contacts.Count && used < MaxEmitters; i++)
            {
                Contact contact = contacts[i];
                Sensor sensor = ResolveSensor(contact.Unit);
                if (sensor == null || !sensor.Any) continue;

                float radarRadius = 0f;
                if (sensor.Radar != null && sensor.Radar.IsOperational())
                {
                    RadarParams parameters = sensor.Radar.RadarParameters;
                    Transform scanner = sensor.Radar.GetScanPoint();
                    // A sensor with no mast falls back to the emitter's own altitude rather
                    // than inventing a horizon the game never checked.
                    float sensorAltitude = scanner != null
                        ? contact.Position.y + scanner.position.y - contact.Unit.transform.position.y
                        : contact.Position.y;
                    radarRadius = ownship == null ? parameters.maxRange : ThreatEnvelope.RadarRadius(
                        new RadarEnvelope(parameters.maxRange, parameters.maxSignal, parameters.minSignal),
                        new TargetSignature(ownRcs, ownPosition.y),
                        sensorAltitude);
                }

                float opticalRadius = 0f;
                if (ownship != null && sensor.Optical != null && sensor.Optical.IsOperational())
                {
                    opticalRadius = ThreatEnvelope.OpticalRadius(
                        sensor.Optical.GetVisualRange(), ownVisibility,
                        sensor.Optical.GetVisualMagnification());
                }

                if (!float.IsFinite(radarRadius)) radarRadius = 0f;
                if (!float.IsFinite(opticalRadius)) opticalRadius = 0f;
                if (radarRadius < MinimumDrawableRadius && opticalRadius < MinimumDrawableRadius) continue;

                samples[used].Position = contact.Position;
                samples[used].RadarRadius = radarRadius;
                samples[used].OpticalRadius = opticalRadius;
                widest = Mathf.Max(widest, Mathf.Max(radarRadius, opticalRadius));
                used++;
            }

            TrackedEmitters = used;
            WidestEnvelopeKm = widest * 0.001f;
            Bake();
        }

        /// <summary>
        /// Every hostile the local side is currently tracking, at the position the faction
        /// believes it is. An untracked emitter is not on this side's picture and heat-maps
        /// nothing — the same rule every other enemy display in this module follows.
        /// </summary>
        private void CollectContacts(FactionHQ hq, Aircraft ownship)
        {
            contacts.Clear();

            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) return;

            Vector3 ownPosition = ownship != null ? ownship.GlobalPosition().AsVector3() : Vector3.zero;
            for (int i = 0; i < all.Count; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit == ownship) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == hq) continue;
                if (!hq.IsTargetBeingTracked(unit)) continue;

                GlobalPosition? known = hq.GetKnownPosition(unit);
                if (!known.HasValue) continue;

                Vector3 position = known.Value.AsVector3();
                float dx = position.x - ownPosition.x;
                float dz = position.z - ownPosition.z;
                if (float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z))
                    contacts.Add(new Contact(unit, position, dx * dx + dz * dz));
            }
        }

        private static Aircraft ResolveOwnship()
        {
            return GameManager.GetLocalPlayer<Player>(out Player player) && player != null &&
                   player.Aircraft != null && !player.Aircraft.disabled
                ? player.Aircraft
                : null;
        }

        /// <summary>
        /// The best radar and the best optical detector on a unit, resolved once and kept:
        /// units do not grow sensors mid-mission, and without the cache this would walk every
        /// tracked unit's hierarchy on every bake. The cache is bounded like the sortie
        /// classifier's emitter cache.
        /// </summary>
        private Sensor ResolveSensor(Unit unit)
        {
            int id = unit.GetInstanceID();
            if (!sensorCache.TryGetValue(id, out Sensor sensor))
            {
                if (sensorCache.Count >= SensorCacheLimit) sensorCache.Clear();
                sensor = new Sensor { Detectors = unit.GetComponentsInChildren<TargetDetector>(true) };
                sensorCache[id] = sensor;
            }
            sensor.Radar = null; sensor.Optical = null;
            TargetDetector[] detectors = sensor.Detectors;
            for (int i = 0; i < detectors.Length; i++)
            {
                TargetDetector detector = detectors[i];
                if (detector == null || !detector.IsOperational()) continue;

                if (detector is Radar radar && radar.GetRadarRange() > 0f &&
                    (sensor.Radar == null || radar.GetRadarRange() > sensor.Radar.GetRadarRange()))
                {
                    sensor.Radar = radar;
                }

                if (detector.GetVisualRange() > 0f &&
                    (sensor.Optical == null || detector.GetVisualRange() > sensor.Optical.GetVisualRange()))
                {
                    sensor.Optical = detector;
                }
            }

            return sensor;
        }

        private void Bake()
        {
            if (graphic == null) return;
            graphic.SetSource(samples, TrackedEmitters, dynamicMap,
                settings != null ? settings.OverlayOpacity.Value : .65f);
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
