using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Night airfield lighting (2026-11-06, idea from Kelly's Client Effects, written from scratch): white runway
    /// edges, blue taxiway edges, a threshold bar that shows green at the end you are landing on and red at the far
    /// one, an approach bar with a sequenced flasher running toward the threshold twice a second, a four-box PAPI on
    /// the left of the touchdown zone (3 degree glide path), and a military rotating beacon (white-white-green) on the
    /// tower. Only the end the camera is approaching shows its approach bar and PAPI. At most <see cref="MaxFields"/>
    /// fields within <see cref="Reach"/> are built; carriers are skipped (they move). Positions are approximate:
    /// the game's airbases carry runway ends and the taxi network, nothing surveyed.
    /// </summary>
    internal sealed class AirfieldLights
    {
        private const float Reach = 18000f, Drop = 22000f, EdgeStep = 60f, TaxiStep = 45f, BarSpacing = 30f;
        private const int MaxFields = 3, Barrettes = 15, MaxTaxiLights = 1500;
        // The steady approach bar is dimmer than the flasher, or its 80 lamps merge into one blob that hides the flasher.
        private static readonly Color32 Steady = new Color32(150, 140, 120, 255);
        private static readonly Color32 White = new Color32(255, 240, 210, 255), Blue = new Color32(25, 70, 190, 255),
            Green = new Color32(60, 255, 120, 255), Red = new Color32(255, 40, 25, 255), Off = new Color32(0, 0, 0, 0);

        private sealed class End
        {
            public Vector3 At, Out;          // threshold (global) and the unit vector pointing away from the runway
            public ParticleSystem Bar, Approach, Rabbit, Papi;
            public Vector3 PapiAt;
            public Color32[] BarColors, RabbitColors = new Color32[Barrettes], PapiColors = new Color32[4];
            public int Lit = -2, PapiMask = -1, BarState = -1;
        }

        private sealed class Strip { public End A, B; public bool AActive, Known; }

        private sealed class Field
        {
            public Airbase Base;
            public GameObject Root;
            public Vector3 Center;
            public ParticleSystem Beacon;
            public ParticleSystemRenderer[] Renderers;
            public bool Shown = true;
            public readonly List<Strip> Strips = new List<Strip>();
            public readonly Color32[] BeaconColor = new Color32[1];
            public int BeaconState = -1;
        }

        private static readonly AccessTools.FieldRef<Airbase, Building> Tower = TowerRef();
        private readonly List<Field> fields = new List<Field>();
        private float nextScan;

        internal int Fields => fields.Count;

        internal void Tick(Vector3 eye, bool enabled, float night, float time)
        {
            bool show = enabled && night > .01f && Datum.origin != null;
            if (!enabled || Datum.origin == null) { Reset(); return; }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 2f;
                Maintain(eye);
            }
            for (int i = 0; i < fields.Count; i++)
            {
                Field f = fields[i];
                if (f.Root == null) continue;
                // Renderers, not SetActive: a paused particle set may come back empty from a disabled GameObject.
                if (f.Shown != show)
                {
                    f.Shown = show;
                    foreach (ParticleSystemRenderer r in f.Renderers) if (r != null) r.enabled = show;
                    foreach (Strip s in f.Strips) { s.A.BarState = s.A.Lit = s.A.PapiMask = s.B.BarState = s.B.Lit = s.B.PapiMask = -2; }
                }
                if (!show) continue;
                foreach (Strip s in f.Strips) Step(s, eye, time);
                Beacon(f, time);
            }
        }

        private void Maintain(Vector3 eye)
        {
            for (int i = fields.Count - 1; i >= 0; i--)
            {
                Field f = fields[i];
                if (f.Root != null && f.Base != null && !f.Base.disabled && (f.Center - eye).sqrMagnitude < Drop * Drop) continue;
                if (f.Root != null) Object.Destroy(f.Root);
                fields.RemoveAt(i);
            }
            if (fields.Count >= MaxFields || FactionRegistry.airbaseLookup == null) return;
            Airbase best = null;
            float bestD = Reach * Reach;
            foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
            {
                if (a == null || a.disabled || a.AttachedAirbase || a.center == null || a.runways == null || a.runways.Length == 0) continue;
                if (fields.Exists(f => f.Base == a)) continue;
                float d = (a.center.GlobalPosition().AsVector3() - eye).sqrMagnitude;
                if (d < bestD) { bestD = d; best = a; }
            }
            if (best == null) return;
            try { fields.Add(Build(best)); }
            catch (System.Exception e) { Plugin.Logger?.LogWarning("[Immersion] Airfield lights skipped " + best.name + ": " + e.Message); }
        }

        private static Field Build(Airbase a)
        {
            var f = new Field { Base = a, Center = a.center.GlobalPosition().AsVector3() };
            f.Root = new GameObject("BoscaliAirfieldLights " + a.name) { hideFlags = HideFlags.DontSave };
            f.Root.transform.SetParent(Datum.origin, false);
            f.Root.transform.localPosition = f.Center;
            Transform root = f.Root.transform;
            var pts = new List<Vector3>();
            var cols = new List<Color32>();
            try
            {
                foreach (Airbase.Runway r in a.runways)
                {
                    if (r == null || r.Start == null || r.End == null) continue;
                    Vector3 s = r.Start.GlobalPosition().AsVector3(), e = r.End.GlobalPosition().AsVector3();
                    Vector3 along = e - s;
                    float length = along.magnitude;
                    if (length < 150f || length > 6000f) continue;
                    along /= length;
                    Vector3 flat = new Vector3(along.x, 0f, along.z).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, flat);
                    float half = Mathf.Clamp(r.GetWidth() * .5f, 6f, 40f);
                    int steps = Mathf.Max(2, Mathf.CeilToInt(length / EdgeStep));
                    for (int i = 0; i <= steps; i++)
                    {
                        Vector3 p = Vector3.Lerp(s, e, i / (float)steps) + Vector3.up * .4f - f.Center;
                        Add(pts, cols, p + side * half, White);
                        Add(pts, cols, p - side * half, White);
                    }
                    f.Strips.Add(new Strip { A = BuildEnd(root, f.Center, s, -flat, side, half, length), B = BuildEnd(root, f.Center, e, flat, side, half, length) });
                }
                Taxiways(a, f.Center, pts, cols);
                LightPoints.Build(root, "Edges", pts, cols, 1.6f, .0022f, .007f);
                f.Beacon = LightPoints.Build(root, "Beacon", new List<Vector3> { BeaconPoint(a) - f.Center }, new List<Color32> { Off }, 6f, .0045f, .014f);
                f.Renderers = f.Root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            }
            catch
            {
                Object.Destroy(f.Root);
                throw;
            }
            return f;
        }

        private static End BuildEnd(Transform root, Vector3 center, Vector3 threshold, Vector3 outward, Vector3 side, float half, float length)
        {
            var end = new End { At = threshold, Out = outward };
            var pts = new List<Vector3>();
            var cols = new List<Color32>();
            Vector3 local = threshold - center + Vector3.up * .5f;
            // Threshold / end bar across the full width, a little beyond the paint.
            for (float x = -half - 8f; x <= half + 8f; x += 3f) Add(pts, cols, local + outward * 2f + side * x, Green);
            end.Bar = LightPoints.Build(root, "Threshold", pts, cols, 1.6f, .0024f, .007f);
            end.BarColors = cols.ToArray();
            // Approach bar: five-lamp barrettes every 30 m, a crossbar at 300 m.
            pts.Clear(); cols.Clear();
            for (int i = 1; i <= Barrettes; i++)
            {
                Vector3 c = local + outward * (i * BarSpacing);
                for (int k = -2; k <= 2; k++) Add(pts, cols, c + side * k, Steady);
                if (i == 10) for (int k = 3; k <= 15; k += 2) { Add(pts, cols, c + side * k, Steady); Add(pts, cols, c - side * k, Steady); }
            }
            end.Approach = LightPoints.Build(root, "Approach", pts, cols, 1f, .0015f, .004f);
            pts.Clear(); cols.Clear();
            for (int i = 1; i <= Barrettes; i++) Add(pts, cols, local + outward * (i * BarSpacing) + Vector3.up * .6f, Off);
            end.Rabbit = LightPoints.Build(root, "Flasher", pts, cols, 3f, .0035f, .012f);
            // PAPI: four boxes on the pilot's left, abeam the touchdown zone; the box nearest the runway is set highest.
            Vector3 left = Vector3.Cross(-outward, Vector3.up);
            end.PapiAt = threshold - outward * Mathf.Min(300f, length * .25f) + left * (half + 15f) + Vector3.up * .8f;
            pts.Clear(); cols.Clear();
            for (int i = 0; i < 4; i++) Add(pts, cols, end.PapiAt - center + left * (i * 9f), Off);
            end.Papi = LightPoints.Build(root, "PAPI", pts, cols, 2.4f, .003f, .009f);
            return end;
        }

        private static void Taxiways(Airbase a, Vector3 center, List<Vector3> pts, List<Color32> cols)
        {
            var network = a.GetTaxiNetwork();
            if (network?.roads == null) return;
            float limit = a.GetRadius() + 300f;
            int count = 0;
            foreach (var road in network.roads)
            {
                if (road?.points == null) continue;
                for (int i = 1; i < road.points.Count && count < MaxTaxiLights; i++)
                {
                    Vector3 p0 = road.points[i - 1].AsVector3() - center, p1 = road.points[i].AsVector3() - center;
                    Vector3 d = p1 - p0;
                    float len = d.magnitude;
                    if (len < 1f || len > 1500f || ((p0 + p1) * .5f).sqrMagnitude > limit * limit) continue;
                    Vector3 side = Vector3.Cross(Vector3.up, d / len).normalized * 7f;
                    for (float t = 0f; t < len && count < MaxTaxiLights; t += TaxiStep, count += 2)
                    {
                        Vector3 p = p0 + d * (t / len) + Vector3.up * .3f;
                        Add(pts, cols, p + side, Blue);
                        Add(pts, cols, p - side, Blue);
                    }
                }
            }
        }

        private static Vector3 BeaconPoint(Airbase a)
        {
            Building building = Tower != null ? Tower(a) : null;
            Transform tower = building != null ? building.transform : null;
            Renderer body = tower != null ? tower.GetComponentInChildren<Renderer>() : null;
            if (body != null) return (body.bounds.center + Vector3.up * (body.bounds.extents.y + 3f)).ToGlobalPosition().AsVector3();
            return a.center.GlobalPosition().AsVector3() + Vector3.up * 20f;
        }

        private static AccessTools.FieldRef<Airbase, Building> TowerRef()
        {
            try { return AccessTools.FieldRefAccess<Airbase, Building>("tower"); }
            catch (System.Exception) { return null; }   // the beacon falls back to the field centre
        }

        private static void Add(List<Vector3> pts, List<Color32> cols, Vector3 p, Color32 c) { pts.Add(p); cols.Add(c); }

        private static void Step(Strip s, Vector3 eye, float time)
        {
            float outA = Vector3.Dot(eye - s.A.At, s.A.Out), outB = Vector3.Dot(eye - s.B.At, s.B.Out);
            // Keep the landing end through the rollout; swap only once the camera is well past the other threshold.
            if (!s.Known) { s.AActive = outA >= outB; s.Known = true; }
            else if (outA > 100f) s.AActive = true;
            else if (outB > 100f) s.AActive = false;
            Face(s.A, s.AActive, outA, eye, time);
            Face(s.B, !s.AActive, outB, eye, time);
        }

        private static void Face(End e, bool landing, float outside, Vector3 eye, float time)
        {
            int bar = landing ? 1 : 0;
            if (e.BarState != bar && e.Bar != null)
            {
                for (int i = 0; i < e.BarColors.Length; i++) e.BarColors[i] = landing ? Green : Red;
                LightPoints.Recolor(e.Bar, e.BarColors);
                e.BarState = bar;
            }
            bool approach = landing && outside > -50f;
            Show(e.Approach, approach);
            Show(e.Rabbit, approach);
            Show(e.Papi, approach);
            if (!approach) return;

            // Sequenced flasher: one barrette at a time from the far end inward, two runs a second.
            int lit = Barrettes - 1 - Mathf.FloorToInt(Mathf.Repeat(time * 2f, 1f) * Barrettes);
            if (lit != e.Lit)
            {
                for (int i = 0; i < Barrettes; i++) e.RabbitColors[i] = i == lit ? White : Off;
                LightPoints.Recolor(e.Rabbit, e.RabbitColors);
                e.Lit = lit;
            }

            Vector3 sight = eye - e.PapiAt;
            float along = Mathf.Max(1f, Vector3.Dot(sight, e.Out));
            float angle = Mathf.Atan2(sight.y, along) * Mathf.Rad2Deg;
            int mask = 0;
            for (int i = 0; i < 4; i++) if (angle >= PapiSetting(i)) mask |= 1 << i;
            if (mask != e.PapiMask)
            {
                for (int i = 0; i < 4; i++) e.PapiColors[i] = (mask & (1 << i)) != 0 ? White : Red;
                LightPoints.Recolor(e.Papi, e.PapiColors);
                e.PapiMask = mask;
            }
        }

        /// <summary>
        /// The runway end the eye is on final for (global coordinates): 300 m to 12 km out, near the extended centreline.
        /// Only built fields count, so it needs AIRFIELD LIGHTS on (by day too; the fields are built day and night).
        /// </summary>
        internal bool TryApproach(Vector3 eye, out Vector3 threshold, out Vector3 outward)
        {
            threshold = outward = Vector3.zero;
            float best = float.MaxValue;
            foreach (Field f in fields)
                foreach (Strip s in f.Strips)
                    for (int k = 0; k < 2; k++)
                    {
                        End e = k == 0 ? s.A : s.B;
                        Vector3 d = eye - e.At;
                        float along = Vector3.Dot(d, e.Out);
                        if (along < 300f || along > 12000f || along >= best) continue;
                        Vector3 flat = new Vector3(d.x, 0f, d.z) - e.Out * along;
                        if (flat.magnitude > 1200f + along * .1f) continue;
                        best = along; threshold = e.At; outward = e.Out;
                    }
            return best < float.MaxValue;
        }

        /// <summary>Box 0 sits nearest the runway at 3.5 degrees; on the 3 degree path two boxes show white, two red.</summary>
        internal static float PapiSetting(int box) => 3.5f - box / 3f;

        private static void Beacon(Field f, float time)
        {
            if (f.Beacon == null) return;
            float t = Mathf.Repeat(time, 2.4f);
            int state = t < .12f || (t > .3f && t < .42f) ? 1 : t > 1.2f && t < 1.5f ? 2 : 0;
            if (state == f.BeaconState) return;
            f.BeaconColor[0] = state == 1 ? White : state == 2 ? Green : Off;
            LightPoints.Recolor(f.Beacon, f.BeaconColor);
            f.BeaconState = state;
        }

        private static void Show(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r.enabled != on) r.enabled = on;
        }

        internal void Reset()
        {
            foreach (Field f in fields) if (f.Root != null) Object.Destroy(f.Root);
            fields.Clear();
            nextScan = 0f;
        }
    }
}
