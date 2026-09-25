using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Orbital;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    internal enum SarPhase : byte
    {
        Idle = 0,
        Collecting = 1,
        Processing = 2,
        Complete = 3
    }

    /// <summary>
    /// Client-local SAR scene formation. Rays are cast from the sensor direction over the
    /// scene's range × azimuth grid during the collect window — a bounded number per frame —
    /// against terrain tiles, structures, units and water. Each hit scatters by surface class
    /// and local incidence and is handed to <see cref="SarImageFormer"/>, which lays heights
    /// over, displaces movers, leaves shadow where no ray landed and speckles the result. The
    /// preview re-forms at a low rate so the scene visibly builds azimuth line by azimuth line.
    /// Gameplay (the host reveal) never depends on this image.
    /// </summary>
    internal sealed class SarCollector
    {
        // 16:10 like the uplink feed, and deliberately coarse: this image is a sensor
        // product, not a photo. Halving the vertical sampling removed most of the ray
        // storm the old 256² grid cost without changing what the image reads as.
        public const int ImageWidth = 192;
        public const int ImageHeight = 120;
        public const float CollectSeconds = 6f;
        public const float ProcessingSeconds = 2f;
        private const int RangeOversample = 192;
        private const int MaximumRaysPerFrame = 400;
        private const float RayLift = 4000f;
        private const float PreviewInterval = 0.8f;
        private const int MaximumClassified = 4096;

        private enum Surface : byte
        {
            Terrain,
            Smooth,
            Structure,
            Target,
            Water
        }

        private readonly Dictionary<int, Surface> surfaces = new Dictionary<int, Surface>();
        private readonly Color32[] pixels = new Color32[ImageWidth * ImageHeight];
        private SarImageFormer former;
        private Vector3 centreLocal;
        private Vector3 los;
        private Vector3 rangeAxis;
        private float windRoughness;
        private int nextRay;
        private int totalRays;
        private float elapsed;
        private float nextPreview;
        private int layerMask;

        public SarPhase Phase { get; private set; }
        public Texture2D Image { get; private set; }
        public float Progress => totalRays > 0 ? Mathf.Clamp01((float)nextRay / totalRays) : 0f;
        public float ProcessingProgress { get; private set; }
        public string Callsign { get; private set; }
        public double SlantRange { get; private set; }
        public int Contacts { get; set; }

        public void Begin(GlobalPosition target, in LookAngles look, in OrbitState state, string callsign,
                          double sceneHalfSize, int seed)
        {
            if (Image == null)
            {
                Image = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGBA32, false)
                {
                    name = "BoscaliSarProduct",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            Vector3 local = target.ToLocalPosition();
            if (Runtime.SupportTargeting.TryMapPoint(target, out Vector3 ground)) local = ground;
            centreLocal = local;

            double sinInc = Math.Sin(look.Incidence), cosInc = Math.Cos(look.Incidence);
            rangeAxis = new Vector3((float)look.AzimuthX, 0f, (float)look.AzimuthZ);
            los = new Vector3((float)(look.AzimuthX * sinInc), (float)cosInc, (float)(look.AzimuthZ * sinInc)).normalized;
            SlantRange = look.SlantRange;
            Callsign = callsign;

            var geometry = new SarGeometry(look.Incidence, look.AzimuthX, look.AzimuthZ, look.SlantRange,
                OrbitMath.Velocity(state.Altitude));
            former = new SarImageFormer(ImageWidth, ImageHeight, sceneHalfSize, geometry, seed);
            totalRays = former.RayCount(RangeOversample);
            nextRay = 0;
            elapsed = 0f;
            nextPreview = 0f;
            ProcessingProgress = 0f;
            Contacts = 0;
            surfaces.Clear();

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float wind = level != null ? level.windSpeed : 5f;
            windRoughness = Mathf.Pow(1f + Mathf.Max(0f, wind) / 6f, 2f);
            layerMask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask |
                        (int)PhysicsLayers.DefaultMask | (int)PhysicsLayers.WaterMask;

            Clear();
            Phase = SarPhase.Collecting;
        }

        public void Tick(float deltaTime)
        {
            if (former == null) return;
            switch (Phase)
            {
                case SarPhase.Collecting:
                {
                    elapsed += deltaTime;
                    int due = Mathf.Min(totalRays, Mathf.CeilToInt(totalRays * Mathf.Clamp01(elapsed / CollectSeconds)));
                    int budget = Mathf.Min(MaximumRaysPerFrame, due - nextRay);
                    for (int i = 0; i < budget; i++) Cast(nextRay++);
                    if (elapsed >= nextPreview)
                    {
                        nextPreview = elapsed + PreviewInterval;
                        Render();
                    }
                    if (nextRay >= totalRays)
                    {
                        Phase = SarPhase.Processing;
                        elapsed = 0f;
                    }
                    break;
                }
                case SarPhase.Processing:
                    elapsed += deltaTime;
                    ProcessingProgress = Mathf.Clamp01(elapsed / ProcessingSeconds);
                    if (elapsed >= ProcessingSeconds)
                    {
                        Render();
                        Phase = SarPhase.Complete;
                    }
                    break;
            }
        }

        public void Dispose()
        {
            if (Image != null) UnityEngine.Object.Destroy(Image);
            Image = null;
            former = null;
            Phase = SarPhase.Idle;
        }

        private void Cast(int index)
        {
            former.PlanRay(index, RangeOversample, out double x, out double z);
            Vector3 ground = centreLocal + new Vector3((float)x, 0f, (float)z);
            Vector3 origin = ground + los * RayLift;

            if (Physics.Raycast(origin, -los, out RaycastHit hit, RayLift * 2f, layerMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 relative = hit.point - centreLocal;
                Surface surface = Classify(hit.collider, hit.point.y);
                double sigma = Backscatter(surface, hit.normal);
                double radial = 0.0;
                if (surface == Surface.Target && hit.rigidbody != null)
                    radial = Vector3.Dot(hit.rigidbody.velocity, los);
                former.Add(relative.x, relative.y, relative.z, sigma, radial);
                return;
            }

            // No surface: the ray reached open water (or the void beyond the map) at sea level.
            float sea = Datum.LocalSeaY;
            if (los.y <= 1e-3f) return;
            float t = (origin.y - sea) / los.y;
            Vector3 water = origin - los * t;
            Vector3 offset = water - centreLocal;
            former.Add(offset.x, offset.y, offset.z, 0.003 * windRoughness, 0.0);
        }

        private Surface Classify(Collider collider, float height)
        {
            if (collider.gameObject.layer == PhysicsLayers.Water || height <= Datum.LocalSeaY + 0.3f) return Surface.Water;
            int id = collider.GetInstanceID();
            if (surfaces.TryGetValue(id, out Surface known)) return known;

            Surface surface;
            if (collider.attachedRigidbody != null && collider.GetComponentInParent<Unit>() != null)
            {
                surface = Surface.Target;
            }
            else
            {
                string name = collider.name;
                if (name.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0) surface = Surface.Terrain;
                else if (name.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("runway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("taxi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("apron", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("pad", StringComparison.OrdinalIgnoreCase) >= 0)
                    surface = Surface.Smooth;
                else surface = Surface.Structure;
            }

            if (surfaces.Count >= MaximumClassified) surfaces.Clear();
            surfaces[id] = surface;
            return surface;
        }

        private double Backscatter(Surface surface, Vector3 normal)
        {
            double cosLocal = Math.Max(0.0, Vector3.Dot(normal, los));
            switch (surface)
            {
                case Surface.Target:
                    return 30.0;
                case Surface.Smooth:
                    // Pavement is a mirror at X-band: almost nothing comes back off-specular.
                    return 0.012 * Math.Pow(cosLocal, 6.0);
                case Surface.Structure:
                {
                    bool wall = Mathf.Abs(normal.y) < 0.35f;
                    if (!wall) return 0.45 * cosLocal + 0.05;
                    // A wall facing the radar forms a dihedral with the ground: a bright line.
                    float facing = Vector3.Dot(new Vector3(normal.x, 0f, normal.z).normalized, rangeAxis);
                    return facing > 0.25f ? 4.0 * facing : 0.04;
                }
                case Surface.Water:
                    return 0.003 * windRoughness;
                default:
                    return 0.2 * Math.Pow(cosLocal, 1.5);
            }
        }

        private void Render()
        {
            if (former == null || Image == null) return;
            // The collect's noise floor rises with range: a steeper, closer look is cleaner.
            double floor = 0.0004 * Math.Pow(SlantRange / 600000.0, 2.0);
            byte[] bytes = former.Form(2, floor);
            // Image rows run top-down; texture rows run bottom-up.
            for (int row = 0; row < ImageHeight; row++)
            {
                int target = (ImageHeight - 1 - row) * ImageWidth;
                int source = row * ImageWidth;
                for (int column = 0; column < ImageWidth; column++)
                {
                    byte v = bytes[source + column];
                    pixels[target + column] = new Color32(v, v, v, 255);
                }
            }
            Image.SetPixels32(pixels);
            Image.Apply(false);
        }

        private void Clear()
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(6, 8, 7, 255);
            Image.SetPixels32(pixels);
            Image.Apply(false);
        }
    }
}
