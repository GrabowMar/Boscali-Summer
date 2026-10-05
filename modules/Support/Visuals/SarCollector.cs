using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Core.Fx;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
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
    /// against terrain, structures and water. Environmental backscatter never includes a Unit
    /// collider: a ray that meets one continues to what lies beneath it. Units appear only through
    /// <see cref="AddApprovedContact(in FeedContact)"/>, fed from the host-revealed faction mirror,
    /// never from a client's local unit registry. Each hit scatters by surface class and local
    /// incidence and is handed to <see cref="SarImageFormer"/>, which lays heights over, displaces
    /// movers, leaves shadow where no ray landed and speckles the result. The preview re-forms at a
    /// low presentation rate so the scene visibly builds azimuth line by azimuth line; the collect
    /// and processing durations run on mission time. Gameplay (the host reveal) never depends on
    /// this image.
    /// </summary>
    internal sealed class SarCollector
    {
        // A bounded, coarse radar product. The accepted scan is independent of gameplay.
        public const int ImageWidth = 128;
        public const int ImageHeight = 80;
        public const float CollectSeconds = 6f;
        public const float ProcessingSeconds = 2f;
        private const int RangeOversample = 128;
        private const int MaximumRaysPerFrame = 400;
        private const float RayLift = 4000f;
        private const float PreviewInterval = 0.8f;
        private const int MaximumClassified = 4096;
        // A ray may pass through this many Unit colliders; a denser stack is not a shadow (that would punch holes that reveal
        // unrevealed bases) but a plain terrain return at the last surface met.
        private const int MaximumUnitSkips = 12;
        private const float UnitSkipStep = 0.05f;

        private enum Surface : byte
        {
            Terrain,
            Smooth,
            Structure,
            Unit,
            Water
        }

        private enum Probe : byte { Hit, Open }

        private readonly Dictionary<int, Surface> surfaces = new Dictionary<int, Surface>();
        private readonly HashSet<int> approved = new HashSet<int>();
        private readonly Color32[] pixels = new Color32[ImageWidth * ImageHeight];
        private SarImageFormer former;
        private Vector3 centreLocal;
        private Vector3 los;
        private Vector3 rangeAxis;
        private float windRoughness;
        private int nextRay;
        private int totalRays;
        private float elapsed;
        private float lastMissionNow;
        private float nextPreview;
        private int layerMask;
        private int rangeOversample = RangeOversample;
        private bool dirty;

        public SarPhase Phase { get; private set; }
        public Texture2D Image { get; private set; }
        public float Progress => totalRays > 0 ? Mathf.Clamp01((float)nextRay / totalRays) : 0f;
        public float ProcessingProgress { get; private set; }
        public double SlantRange { get; private set; }
        public int Contacts { get; set; }

        /// <summary>
        /// False while the product is not on screen: formation continues on mission time but the texture is not rebuilt
        /// until it is visible again (one render then catches up).
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <param name="missionNow">Mission time at the start of the collect (<c>SupportManager.MissionNow()</c>).</param>
        public void Begin(GlobalPosition target, in LookAngles look, in OrbitState state,
                          double sceneHalfSize, int seed, float missionNow)
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

            var geometry = new SarGeometry(look.Incidence, look.AzimuthX, look.AzimuthZ, look.SlantRange,
                OrbitMath.Velocity(state.Altitude));
            former = new SarImageFormer(ImageWidth, ImageHeight, sceneHalfSize, geometry, seed);
            rangeOversample = Mathf.Max(32, Mathf.RoundToInt(
                RangeOversample * FxBus.Scales.RenderTargets));
            totalRays = former.RayCount(rangeOversample);
            nextRay = 0;
            elapsed = 0f;
            lastMissionNow = missionNow;
            nextPreview = 0f;
            ProcessingProgress = 0f;
            Contacts = 0;
            dirty = false;
            surfaces.Clear();
            approved.Clear();

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float wind = level != null ? level.windSpeed : 5f;
            windRoughness = Mathf.Pow(1f + Mathf.Max(0f, wind) / 6f, 2f);
            layerMask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.WaterMask;

            Clear();
            Phase = SarPhase.Collecting;
        }

        /// <summary>
        /// Advance on mission time: a pause holds the collect, and a clock re-baseline never steps it back. Only the low-rate
        /// preview texture upload follows presentation time.
        /// </summary>
        public void Tick(float missionNow)
        {
            if (former == null) return;
            float delta = missionNow > lastMissionNow ? missionNow - lastMissionNow : 0f;
            lastMissionNow = missionNow;
            switch (Phase)
            {
                case SarPhase.Collecting:
                {
                    elapsed += delta;
                    int due = Mathf.Min(totalRays, Mathf.CeilToInt(totalRays * Mathf.Clamp01(elapsed / CollectSeconds)));
                    int budget = Mathf.Min(Mathf.Max(50, Mathf.RoundToInt(
                        MaximumRaysPerFrame * FxBus.Scales.RenderTargets)), due - nextRay);
                    for (int i = 0; i < budget; i++) Cast(nextRay++);
                    if (budget > 0) dirty = true;
                    if (Time.unscaledTime >= nextPreview && dirty && Visible)
                    {
                        nextPreview = Time.unscaledTime + PreviewInterval;
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
                    elapsed += delta;
                    ProcessingProgress = Mathf.Clamp01(elapsed / ProcessingSeconds);
                    if (elapsed >= ProcessingSeconds)
                    {
                        dirty = true;
                        if (Visible) Render();
                        Phase = SarPhase.Complete;
                    }
                    break;
                case SarPhase.Complete:
                    if (dirty && Visible) Render(); // an approved contact arrived late, or the view was hidden at completion
                    break;
            }
        }

        /// <summary>Feed close or scene reset: the texture, the former and every approved id go.</summary>
        public void Dispose()
        {
            if (Image != null) UnityEngine.Object.Destroy(Image);
            Image = null;
            former = null;
            surfaces.Clear();
            approved.Clear();
            Contacts = 0;
            dirty = false;
            Phase = SarPhase.Idle;
        }

        /// <summary>
        /// A host-revealed ground contact as a SAR return. <paramref name="relativeX"/>, <paramref name="relativeY"/> and
        /// <paramref name="relativeZ"/> are metres from the scene centre. This and its overload are the only way a Unit
        /// contributes to the image. False when no collect is running, a value is not finite or the bound is reached.
        /// </summary>
        public bool AddApprovedContact(float relativeX, float relativeY, float relativeZ, float radialVelocity, float sigma)
        {
            if (former == null || Phase == SarPhase.Idle || Contacts >= SpaceWire.MaxContacts ||
                !Finite(relativeX) || !Finite(relativeY) || !Finite(relativeZ) || !Finite(radialVelocity) || !Finite(sigma) || sigma <= 0f)
                return false;
            former.Add(relativeX, relativeY, relativeZ, sigma, radialVelocity);
            Contacts++;
            dirty = true;
            return true;
        }

        /// <summary>
        /// A mirror row (host-revealed, the faction's view) placed at its authorised global coordinates, on the ground under
        /// them. Each contact id contributes once per collect.
        /// </summary>
        public bool AddApprovedContact(in FeedContact row)
        {
            if (former == null || !Finite(row.X) || !Finite(row.Z) || approved.Contains(row.Id) || approved.Count >= SpaceWire.MaxContacts)
                return false;
            var point = new GlobalPosition(row.X, 0f, row.Z);
            Vector3 local = point.ToLocalPosition();
            if (Runtime.SupportTargeting.TryMapPoint(point, out Vector3 ground)) local = ground;
            Vector3 relative = local - centreLocal;
            if (!AddApprovedContact(relative.x, relative.y, relative.z, SpaceFeedRules.SarContactRadial(row.Moving),
                SpaceFeedRules.SarApprovedSigma)) return false;
            approved.Add(row.Id);
            return true;
        }

        /// <summary>Where a mirror row falls in the SAR picture (0..1, origin bottom-left, like a RawImage), false when it is outside it.</summary>
        public bool TryProject(in FeedContact row, out Vector2 uv)
        {
            uv = default;
            if (former == null || !Finite(row.X) || !Finite(row.Z)) return false;
            var point = new GlobalPosition(row.X, 0f, row.Z);
            Vector3 local = point.ToLocalPosition();
            if (Runtime.SupportTargeting.TryMapPoint(point, out Vector3 ground)) local = ground;
            Vector3 relative = local - centreLocal;
            if (!former.Project(relative.x, relative.y, relative.z, SpaceFeedRules.SarContactRadial(row.Moving), out int column, out int rowIndex)) return false;
            uv = new Vector2((column + 0.5f) / ImageWidth, (ImageHeight - 1 - rowIndex + 0.5f) / ImageHeight);
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void Cast(int index)
        {
            former.PlanRay(index, rangeOversample, out double x, out double z);
            Vector3 ground = centreLocal + new Vector3((float)x, 0f, (float)z);
            Vector3 origin = ground + los * RayLift;

            Probe probe = Trace(origin, out RaycastHit hit, out bool exhausted);
            if (probe == Probe.Hit)
            {
                Vector3 relative = hit.point - centreLocal;
                // Skips used up under a stack of Unit colliders: a plain terrain return, never a shadow and never a unit.
                Surface surface = exhausted ? Surface.Terrain : Classify(hit.collider, hit.point.y);
                former.Add(relative.x, relative.y, relative.z, Backscatter(surface, exhausted ? Vector3.up : hit.normal), 0.0);
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

        /// <summary>The first terrain, structure or water surface along the ray, looking through Unit colliders.</summary>
        private Probe Trace(Vector3 origin, out RaycastHit hit, out bool exhausted)
        {
            float remaining = RayLift * 2f;
            exhausted = false;
            RaycastHit last = default;
            for (int skips = 0; skips <= MaximumUnitSkips; skips++)
            {
                if (!Physics.Raycast(origin, -los, out hit, remaining, layerMask, QueryTriggerInteraction.Ignore)) return Probe.Open;
                if (Classify(hit.collider, hit.point.y) != Surface.Unit) return Probe.Hit;
                last = hit;
                float advance = hit.distance + UnitSkipStep;
                origin -= los * advance;
                remaining -= advance;
                if (remaining <= 0f) break;
            }
            hit = last;
            exhausted = true;
            return Probe.Hit;
        }

        private Surface Classify(Collider collider, float height)
        {
            if (collider.gameObject.layer == PhysicsLayers.Water) return Surface.Water;
            int id = collider.GetInstanceID();
            if (!surfaces.TryGetValue(id, out Surface known))
            {
                if (collider.GetComponentInParent<Unit>() != null)
                {
                    known = Surface.Unit;
                }
                else
                {
                    string name = collider.name;
                    if (name.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0) known = Surface.Terrain;
                    else if (name.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("runway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("taxi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("apron", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("pad", StringComparison.OrdinalIgnoreCase) >= 0)
                        known = Surface.Smooth;
                    else known = Surface.Structure;
                }
                if (surfaces.Count >= MaximumClassified) surfaces.Clear();
                surfaces[id] = known;
            }
            // A Unit is never environment, even where it floats; sea level otherwise reads as water.
            if (known != Surface.Unit && height <= Datum.LocalSeaY + 0.3f) return Surface.Water;
            return known;
        }

        private double Backscatter(Surface surface, Vector3 normal)
        {
            double cosLocal = Math.Max(0.0, Vector3.Dot(normal, los));
            switch (surface)
            {
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
            dirty = false;
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
