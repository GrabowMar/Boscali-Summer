using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Domain
{
    /// <summary>One damage point of a sea structure: a spine (a section's core) or a local detail group.</summary>
    internal sealed class SeaPointPlan
    {
        public string Name;
        public int Section;
        public int Parent = -1;          // index into the plan; -1 for the root spine
        public bool Spine;
        public bool Required = true;
        public int[] Pieces;             // indices into the section's Pieces
        public float Capacity = 100f;    // native UnitPart hit points
        public float Toughness = 1f;     // 1 = donor hull armor; below 1 takes damage faster
        public Vector3 Min, Max;         // section-local bounds of the point's geometry

        public Vector3 Centre => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
        public int Triangles { get; internal set; }
    }

    /// <summary>
    /// Procedural damage graph for a sea structure. One spine per authored section owns its core pieces;
    /// the remaining pieces are grouped into local detail points by logical assembly and proximity. The
    /// plan is a pure function of the model: the same file produces the same points and parent order on
    /// every peer, with no random numbers. Pinned by the offline tests.
    /// </summary>
    internal static class SeaDamagePlanner
    {        public const int MaxPoints = 64;
        public const int MaxPerSection = 7;
        public const float JoinDistance = 12f;

        public static List<SeaPointPlan> Plan(SeaLayout layout)
        {
            var plans = new List<SeaPointPlan>(MaxPoints);
            if (layout == null || layout.Sections == null) return plans;

            // Spines first, in section order (the model guarantees a parent precedes its child).
            var spine = new int[layout.Sections.Length];
            for (int s = 0; s < layout.Sections.Length; s++)
            {
                SeaSection section = layout.Sections[s];
                var pieces = new List<int>();
                for (int p = 0; p < section.Pieces.Length; p++)
                    if (section.Pieces[p].Core || section.Pad) pieces.Add(p);
                if (pieces.Count == 0 && section.Pieces.Length > 0) pieces.Add(0);
                int parent = section.Parent >= 0 ? spine[section.Parent] : -1;
                plans.Add(Point(section, s, parent, "HULL", pieces, true, s != 0, 1.1f));
                spine[s] = plans.Count - 1;
            }

            // Detail points per section, parented to that section's spine. The flight deck stays one hull
            // point: extra boxes on its landing surface would block native landing gear rays and AI approaches.
            for (int s = 0; s < layout.Sections.Length; s++)
            {
                SeaSection section = layout.Sections[s];
                if (section.Pad) continue;
                var loose = new List<int>();
                for (int p = 0; p < section.Pieces.Length; p++)
                    if (!section.Pieces[p].Core) loose.Add(p);
                if (loose.Count == 0) continue;

                List<List<int>> groups = Group(section, loose, MaxPerSection);
                for (int g = 0; g < groups.Count; g++)
                    plans.Add(Point(section, s, spine[s], "POINT " + (g + 1).ToString("00"), groups[g], false, true,
                        ToughnessOf(section, groups[g])));
            }

            // Keep the byte-sized native part budget honest even on a future detailed model: merge the
            // smallest detail into its parent until the cap holds. Spines are never merged.
            while (plans.Count > MaxPoints)
            {
                int smallest = -1;
                for (int i = 0; i < plans.Count; i++)
                {
                    if (plans[i].Spine) continue;
                    if (smallest < 0 || plans[i].Triangles < plans[smallest].Triangles) smallest = i;
                }
                if (smallest < 0) break;
                SeaPointPlan detail = plans[smallest];
                SeaPointPlan parent = plans[detail.Parent];
                parent.Pieces = Merge(parent.Pieces, detail.Pieces);
                Extend(parent, detail);
                for (int i = 0; i < plans.Count; i++)
                    if (plans[i].Parent == smallest) plans[i].Parent = detail.Parent;
                // Nothing is parented to a detail point by construction; repair anyway for the future.
                for (int i = 0; i < plans.Count; i++)
                    if (plans[i].Parent > smallest) plans[i].Parent--;
                plans.RemoveAt(smallest);
            }
            return plans;
        }

        private static SeaPointPlan Point(SeaSection section, int index, int parent, string label, List<int> pieces,
            bool spine, bool required, float toughness)
        {
            var point = new SeaPointPlan
            {
                Name = section.Name + " " + label,
                Section = index,
                Parent = parent,
                Spine = spine,
                Required = required,
                Pieces = pieces.ToArray(),
                Toughness = toughness,
                Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                Max = new Vector3(float.MinValue, float.MinValue, float.MinValue)
            };
            foreach (int p in point.Pieces) Extend(point, section.Pieces[p]);
            point.Capacity = CapacityOf(point.Triangles, spine);
            return point;
        }

        private static List<List<int>> Group(SeaSection section, List<int> loose, int cap)
        {
            var groups = new List<List<int>>();
            foreach (int piece in loose)
            {
                int best = -1;
                float bestDistance = JoinDistance;
                Vector3 at = section.Pieces[piece].Centre;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (groups[g].Count >= 10) continue;
                    float distance = (Centre(section, groups[g]) - at).magnitude;
                    if (distance > bestDistance) continue;
                    bestDistance = distance;
                    best = g;
                }
                if (best >= 0) { groups[best].Add(piece); continue; }
                groups.Add(new List<int> { piece });
            }
            // Near-boundary pieces that formed their own group fall back into the nearest one.
            while (groups.Count > cap)
            {
                int smallest = 0;
                for (int g = 1; g < groups.Count; g++) if (groups[g].Count < groups[smallest].Count) smallest = g;
                int nearest = -1;
                float distance = float.MaxValue;
                Vector3 at = Centre(section, groups[smallest]);
                for (int g = 0; g < groups.Count; g++)
                {
                    if (g == smallest) continue;
                    float d = (Centre(section, groups[g]) - at).magnitude;
                    if (d >= distance) continue;
                    distance = d;
                    nearest = g;
                }
                if (nearest < 0) break;
                groups[nearest].AddRange(groups[smallest]);
                groups.RemoveAt(smallest);
            }
            return groups;
        }

        private static Vector3 Centre(SeaSection section, List<int> pieces)
        {
            Vector3 sum = Vector3.zero;
            foreach (int p in pieces) sum += section.Pieces[p].Centre;
            return pieces.Count > 0 ? sum / pieces.Count : Vector3.zero;
        }

        private static void Extend(SeaPointPlan point, SeaPiece piece)
        {
            point.Triangles += piece.Triangles.Length / 3;
            foreach (Vector3 v in piece.Vertices)
            {
                point.Min = Vector3.Min(point.Min, v);
                point.Max = Vector3.Max(point.Max, v);
            }
        }

        private static void Extend(SeaPointPlan point, SeaPointPlan other)
        {
            point.Min = Vector3.Min(point.Min, other.Min);
            point.Max = Vector3.Max(point.Max, other.Max);
            point.Triangles += other.Triangles;
        }

        private static int[] Merge(int[] a, int[] b)
        {
            var all = new int[a.Length + b.Length];
            Array.Copy(a, all, a.Length);
            Array.Copy(b, 0, all, a.Length, b.Length);
            return all;
        }

        private static float CapacityOf(int triangles, bool spine)
        {
            if (spine) return 130f;
            return Mathf.Clamp(55f + triangles * 0.018f, 55f, 130f);
        }

        private static float ToughnessOf(SeaSection section, List<int> pieces)
        {
            int triangles = 0;
            foreach (int p in pieces) triangles += section.Pieces[p].Triangles.Length / 3;
            return Mathf.Clamp(0.55f + triangles / 6000f, 0.55f, 1.1f);
        }
    }

    /// <summary>The same plan for a kind, computed once per peer from the embedded model.</summary>
    internal static class SeaDamagePlans
    {
        private static readonly List<SeaPointPlan>[] Cache = new List<SeaPointPlan>[2];

        public static List<SeaPointPlan> Of(SeaKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= Cache.Length) return new List<SeaPointPlan>();
            return Cache[index] ?? (Cache[index] = SeaDamagePlanner.Plan(SeaLayouts.Of(kind)));
        }
    }
}
