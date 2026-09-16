using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics.Ui;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// The hostile sensor picture as heat: one soft raster over the theater map whose intensity at
    /// a spot is how strongly the best tracked hostile sensor would see the player's own aircraft
    /// there. A near radar is hotter than a distant one, everything a single emitter covers merges
    /// into one blob instead of stacking outlines, and overlapping coverage reads as one hotter
    /// region.
    ///
    /// <para>Client-local presentation only. It reads the faction's own tracking database, so it
    /// shows the picture the player's side actually holds — an untracked emitter contributes no
    /// heat, and a contact's blob sits on the position the faction believes it is at, never on a
    /// live transform. No network traffic, no world mutation, no game state changed.</para>
    ///
    /// <para>Bounded and cheap by construction: one texture on one quad, no per-emitter objects,
    /// and no per-frame work at all — the raster is stretch-anchored to the map, so pan and zoom
    /// transform it for free. A bake clears a flat pre-allocated buffer, splats one bounded disc
    /// per tracked emitter inside its own bounding box, and recolours; it runs once per
    /// <see cref="RefreshSeconds"/> and sleeps entirely while the map is closed.</para>
    ///
    /// <para>Coverage is drawn as the radio-horizon and scan-limit picture, and it is the
    /// optimistic one: look-down clutter only subtracts from the signal, and terrain line of sight
    /// is a per-bearing cut this raster does not pay a raycast fan for. Both omissions overstate
    /// coverage, which is the safe direction for a warning.</para>
    /// </summary>
    internal sealed class ThreatMapOverlay : MonoBehaviour, ISceneService
    {
        private const int MaxEmitters = 32;
        private const int SensorCacheLimit = 512;
        private const float RefreshSeconds = 1f;
        private const float MinimumDrawableRadius = 1f;

        /// <summary>Longest side of the heat raster in pixels; the field is smooth, not crisp.</summary>
        private const int HeatTextureMax = 256;

        /// <summary>
        /// Peak alpha gain over <c>OverlayOpacity</c>, so heat answers the same slider. The
        /// per-pixel alpha is the squared heat, not the heat: an envelope here is wider than the
        /// theater, so the falloff's own curve is not enough to keep the far field quiet.
        /// </summary>
        private const float HeatAlphaGain = 1.2f;

        /// <summary>Below this the field is a tint, not information, and stays transparent.</summary>
        private const float HeatFloor = 0.02f;

        /// <summary>Optical/IR coverage is the quieter threat: same field, half the intensity.</summary>
        private const float OpticalWeight = 0.5f;

        private const float FallbackTheaterMetres = 81920f;

        private static readonly Color32 ClearPixel = new Color32(0, 0, 0, 0);

        private sealed class Sensor
        {
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
        private ManualLogSource logger;

        private DynamicMap dynamicMap;
        private GameObject root;
        private RectTransform rootRect;
        private RawImage fieldImage;
        private Texture2D fieldTexture;
        private float[] field;
        private Color32[] pixels;
        private readonly List<Contact> contacts = new List<Contact>(MaxEmitters * 2);
        private readonly Dictionary<int, Sensor> sensorCache = new Dictionary<int, Sensor>(MaxEmitters * 4);
        private readonly Sample[] samples = new Sample[MaxEmitters];

        private Vector2 theater;
        private float nextRefresh;
        private bool initialized;

        /// <summary>True once the map is on screen and the overlay can be drawn at all.</summary>
        public bool Available => initialized && dynamicMap != null;

        /// <summary>How many tracked emitters the last bake contributed heat.</summary>
        public int TrackedEmitters { get; private set; }

        /// <summary>
        /// The widest envelope among them, in kilometres. A ground radar's radio horizon against
        /// a high target reaches past the far map edge on this scale, so the map is hot around
        /// the emitters rather than only inside a ring: this is the number that says so.
        /// </summary>
        public float WidestEnvelopeKm { get; private set; }

        public bool Visible => settings != null && settings.ThreatHeat.Value;

        public void SetVisible(bool visible)
        {
            if (settings == null || settings.ThreatHeat.Value == visible) return;
            settings.ThreatHeat.Value = visible;
            nextRefresh = 0f;
        }

        public void Configure(CommandSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
        }

        public void ResetForScene()
        {
            if (root != null)
            {
                Destroy(root);
                root = null;
            }

            Destroy(fieldTexture);
            fieldTexture = null;
            fieldImage = null;
            field = null;
            pixels = null;

            contacts.Clear();
            sensorCache.Clear();
            rootRect = null;
            dynamicMap = null;
            TrackedEmitters = 0;
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
            if (!initialized) TryInitialize();

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

            theater = TheaterFrame.Resolve(dynamicMap);
            if (theater.x <= 1f || theater.y <= 1f)
            {
                theater = new Vector2(FallbackTheaterMetres, FallbackTheaterMetres);
                logger?.LogWarning("[COM] Threat heat could not resolve the theater span; " +
                    "falling back to " + FallbackTheaterMetres + "m.");
            }

            GetTextureSize(out int width, out int height);
            EnsureTexture(width, height);

            root = new GameObject("BoscaliThreatHeat", typeof(RectTransform), typeof(RawImage));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(dynamicMap.mapImage.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.localScale = Vector3.one;
            rootRect.localPosition = Vector3.zero;

            fieldImage = root.GetComponent<RawImage>();
            fieldImage.texture = fieldTexture;
            fieldImage.raycastTarget = false;

            // Just above the territory tint, below the trench trace, the frontline and the map's
            // own symbols: a soft ground-cover wash, not a line and not a marker.
            root.transform.SetSiblingIndex(dynamicMap.mapImage.transform.childCount > 1 ? 1 : 0);

            SetShown(true);
            initialized = true;
            logger?.LogInfo("[COM] Threat heat overlay initialized (" + width + "x" + height +
                " field over a " + Mathf.RoundToInt(theater.x) + "x" + Mathf.RoundToInt(theater.y) +
                "m theater, " + MaxEmitters + " emitters maximum).");
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
            if (hq == null || ownship == null)
            {
                TrackedEmitters = 0;
                WidestEnvelopeKm = 0f;
                Bake();
                return;
            }

            CollectContacts(hq, ownship);
            if (contacts.Count > 1) contacts.Sort(NearestFirst);

            Vector3 ownPosition = ownship.GlobalPosition().AsVector3();
            float ownRcs = ownship.RCS;
            float ownVisibility = ownship.GetVisibility();

            int used = 0;
            float widest = 0f;
            for (int i = 0; i < contacts.Count && used < MaxEmitters; i++)
            {
                Contact contact = contacts[i];
                Sensor sensor = ResolveSensor(contact.Unit);
                if (sensor == null || !sensor.Any) continue;

                float radarRadius = 0f;
                if (sensor.Radar != null)
                {
                    RadarParams parameters = sensor.Radar.RadarParameters;
                    Transform scanner = sensor.Radar.GetScanPoint();
                    // A sensor with no mast falls back to the emitter's own altitude rather
                    // than inventing a horizon the game never checked.
                    float sensorAltitude = scanner != null
                        ? scanner.GlobalPosition().AsVector3().y
                        : contact.Position.y;
                    radarRadius = ThreatEnvelope.RadarRadius(
                        new RadarEnvelope(parameters.maxRange, parameters.maxSignal, parameters.minSignal),
                        new TargetSignature(ownRcs, ownPosition.y),
                        sensorAltitude);
                }

                float opticalRadius = 0f;
                if (sensor.Optical != null)
                {
                    opticalRadius = ThreatEnvelope.OpticalRadius(
                        sensor.Optical.GetVisualRange(), ownVisibility,
                        sensor.Optical.GetVisualMagnification());
                }

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

            Vector3 ownPosition = ownship.GlobalPosition().AsVector3();
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
            if (sensorCache.TryGetValue(id, out Sensor cached)) return cached;
            if (sensorCache.Count >= SensorCacheLimit) return null;

            Sensor sensor = new Sensor();
            TargetDetector[] detectors = unit.GetComponentsInChildren<TargetDetector>(true);
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

            sensorCache[id] = sensor;
            return sensor;
        }

        // -------------------------------------------------------------------- raster

        private void GetTextureSize(out int width, out int height)
        {
            float longest = Mathf.Max(theater.x, theater.y);
            width = Mathf.Clamp(Mathf.RoundToInt(HeatTextureMax * theater.x / longest), 1, HeatTextureMax);
            height = Mathf.Clamp(Mathf.RoundToInt(HeatTextureMax * theater.y / longest), 1, HeatTextureMax);
        }

        private void EnsureTexture(int width, int height)
        {
            if (fieldTexture != null && fieldTexture.width == width && fieldTexture.height == height) return;

            Destroy(fieldTexture);

            fieldTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "BoscaliThreatHeat",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            field = new float[width * height];
            pixels = new Color32[width * height];
            Array.Clear(pixels, 0, pixels.Length);
            fieldTexture.SetPixels32(pixels);
            fieldTexture.Apply(false);

            if (fieldImage != null) fieldImage.texture = fieldTexture;
        }

        private void Bake()
        {
            if (fieldTexture == null || field == null) return;

            int width = fieldTexture.width;
            int height = fieldTexture.height;
            Array.Clear(field, 0, field.Length);

            for (int i = 0; i < TrackedEmitters; i++)
            {
                Sample sample = samples[i];
                Splat(sample.Position, sample.RadarRadius, 1f, width, height);
                Splat(sample.Position, sample.OpticalRadius, OpticalWeight, width, height);
            }

            Colorize();
            fieldTexture.SetPixels32(pixels);
            fieldTexture.Apply(false);
        }

        /// <summary>
        /// One emitter's coverage disc, sampled only inside its own bounding box and merged by
        /// maximum, so overlapping emitters never brighten each other into a false absolute.
        /// </summary>
        private void Splat(Vector3 centre, float radius, float weight, int width, int height)
        {
            if (radius < MinimumDrawableRadius) return;

            float cellX = theater.x / (width - 1);
            float cellY = theater.y / (height - 1);
            float halfX = theater.x * 0.5f;
            float halfY = theater.y * 0.5f;

            int minX = Mathf.Max(0, Mathf.FloorToInt((centre.x - radius + halfX) / cellX));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt((centre.x + radius + halfX) / cellX));
            int minY = Mathf.Max(0, Mathf.FloorToInt((centre.z - radius + halfY) / cellY));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt((centre.z + radius + halfY) / cellY));
            float radiusSquared = radius * radius;

            for (int y = minY; y <= maxY; y++)
            {
                float dz = y * cellY - halfY - centre.z;
                float dzSquared = dz * dz;
                int row = y * width;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x * cellX - halfX - centre.x;
                    float distanceSquared = dx * dx + dzSquared;
                    if (distanceSquared >= radiusSquared) continue;

                    float heat = ThreatEnvelope.Heat01(radius, Mathf.Sqrt(distanceSquared)) * weight;
                    int index = row + x;
                    if (heat > field[index]) field[index] = heat;
                }
            }
        }

        private void Colorize()
        {
            float alpha = HeatAlphaGain * (settings != null ? settings.OverlayOpacity.Value : 0.35f);
            Color cool = AvTheme.RailCaution;
            Color hot = AvTheme.RailDanger;
            float coolR = cool.r * 255f, coolG = cool.g * 255f, coolB = cool.b * 255f;
            float hotR = hot.r * 255f, hotG = hot.g * 255f, hotB = hot.b * 255f;

            for (int i = 0; i < field.Length; i++)
            {
                float heat = field[i];
                if (heat < HeatFloor)
                {
                    pixels[i] = ClearPixel;
                    continue;
                }

                if (heat > 1f) heat = 1f;
                pixels[i] = new Color32(
                    (byte)(coolR + (hotR - coolR) * heat),
                    (byte)(coolG + (hotG - coolG) * heat),
                    (byte)(coolB + (hotB - coolB) * heat),
                    (byte)(Mathf.Clamp01(heat * heat * alpha) * 255f));
            }
        }
    }
}
