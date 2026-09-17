using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Places small infantry-scale game scenery along a position's ditches — HESCO, sandbag
    /// and light gabion pieces only, never vehicle-scale encampments. Pieces sit on the
    /// trench line itself, unlocked as the position is built out, bounded, networked and
    /// removed with the position. Assets are resolved from the game's own encyclopedia at
    /// runtime and filtered by footprint and keyword, so a game update that resizes or
    /// renames a piece degrades to ditch-only instead of dropping a bunker in a field.
    /// </summary>
    internal sealed class TrenchWorks
    {
        internal const string Prefix = "BoscaliSummer:TrenchWork:";
        internal const int MaximumWorks = 8;
        private const float MaximumFootprint = 6f;
        private const int CatalogCeiling = 4;
        private const int NearMissCeiling = 10;

        private static readonly string[] Keywords = { "hesco", "sandbag", "gabion", "dugout" };
        private static readonly string[] Excluded = { "hulldown", "shelter", "concrete", "camonet", "wall" };

        private static List<UnitDefinition> catalog;
        private static Encyclopedia catalogSource;
        private static bool catalogReported;

        private readonly Scenery[] works = new Scenery[MaximumWorks];
        private readonly bool[] spawned = new bool[MaximumWorks];
        private readonly List<Vector3> line = new List<Vector3>(32);
        private readonly TrenchLine position;

        internal int Count { get; private set; }

        internal TrenchWorks(TrenchLine trenchLine)
        {
            position = trenchLine;
        }

        /// <summary>Adds the works unlocked by <paramref name="stage"/>; safe to call repeatedly.</summary>
        internal void Deploy(TrenchStage stage)
        {
            if (position == null || position.Overrun) return;
            int target = TrenchTraceMath.WorksBudget(stage);
            if (target == 0) return;

            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return;

            List<UnitDefinition> pieces = ResolveCatalog();
            if (pieces == null) return;  // Encyclopedia not loaded yet: retry later.
            if (pieces.Count == 0) return; // No infantry-scale pieces in this build.

            for (int i = 0; i < target && i < MaximumWorks; i++)
            {
                if (spawned[i]) continue;
                if (Spawn(i, spawner, pieces)) spawned[i] = true;
            }
        }

        private bool Spawn(int index, Spawner spawner, List<UnitDefinition> pieces)
        {
            bool support = index >= MaximumWorks / 2;
            CollectLine(support);
            if (line.Count == 0) return true; // Nothing to attach to; do not retry forever.

            int rank = index % (MaximumWorks / 2);
            Vector3 anchor = line[Mathf.Clamp((rank + 1) * line.Count / 5, 0, line.Count - 1)];
            Vector3 forward = position.ThreatAt(anchor);
            // Even slots are fire positions on the parapet crest (just forward of the centreline,
            // overlooking no man's land); odd slots are the sandbag shelters and dugouts just
            // behind the parados, clear of the now ~1m rear skirt instead of the old ~5m one.
            Vector3 desired = anchor + forward * (rank % 2 == 0 ? 1.1f : -2.6f);
            Vector3 ground = TrenchTerrain.TryGround(desired, out Vector3 sampled) ? sampled : anchor;

            UnitDefinition piece = pieces[index % pieces.Count];
            Scenery scenery = spawner.SpawnScenery(piece.unitPrefab, new GlobalPosition(ground),
                Quaternion.LookRotation(forward), Prefix + position.Id + ":" + index);
            if (scenery == null) return false;
            works[index] = scenery;
            Count++;
            return true;
        }

        private void CollectLine(bool support)
        {
            line.Clear();
            Vector3[] anchors = support ? position.SupportAnchors : position.Anchors;
            if (anchors == null) return;
            for (int i = 0; i < anchors.Length && line.Count < 32; i++) line.Add(anchors[i]);
        }

        private static List<UnitDefinition> ResolveCatalog()
        {
            // The static Lookup dictionary is not populated on every host; the instance lists
            // are, once the encyclopedia has loaded. Null still means "not loaded yet: retry".
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null) return null;
            if (catalog != null && ReferenceEquals(catalogSource, encyclopedia)) return catalog;

            catalogSource = encyclopedia;
            catalog = new List<UnitDefinition>(CatalogCeiling);
            var nearMiss = new List<string>(NearMissCeiling);
            int examined = Scan(encyclopedia.scenery, nearMiss, 0);
            Scan(encyclopedia.otherUnits, nearMiss, examined);
            catalog.Sort((a, b) => Footprint(a).CompareTo(Footprint(b)));
            if (catalog.Count > CatalogCeiling) catalog.RemoveRange(CatalogCeiling, catalog.Count - CatalogCeiling);
            if (!catalogReported)
            {
                catalogReported = true;
                if (catalog.Count == 0)
                    Debug.LogWarning("[TrenchWorks] No infantry-scale scenery pieces found; sectors stay ditch-only.");
                else
                    Debug.Log("[TrenchWorks] Infantry works: " + string.Join(", ",
                        catalog.ConvertAll(d => d.jsonKey + " (" + Footprint(d).ToString("0.0") + "m)").ToArray()));
                // Bounded evidence for widening the keyword list when nothing matched.
                if (nearMiss.Count > 0)
                    Debug.LogWarning("[TrenchWorks] Scenery near-miss (keyword or exclusion rejected): " +
                        string.Join(", ", nearMiss.ToArray()));
            }
            return catalog;
        }

        /// <summary>
        /// One pass over a definition list: root-scenery pieces within the footprint enter
        /// the catalog; keyword or exclusion near-misses are remembered (bounded) as
        /// evidence for widening the keyword list. <paramref name="examined"/> is carried
        /// across lists so the total scan stays under the shared ceiling.
        /// </summary>
        private static int Scan(IReadOnlyList<UnitDefinition> source, List<string> nearMiss, int examined)
        {
            if (source == null) return examined;
            for (int i = 0; i < source.Count && examined < 4096; i++, examined++)
            {
                UnitDefinition definition = source[i];
                if (definition?.unitPrefab == null || definition.unitPrefab.GetComponent<Scenery>() == null) continue;
                if (Footprint(definition) > MaximumFootprint) continue;
                if (!IsInfantryPiece(definition))
                {
                    if (nearMiss.Count < NearMissCeiling)
                        nearMiss.Add((definition.jsonKey ?? "?") + " \"" + (definition.unitName ?? "?") + "\"");
                    continue;
                }
                catalog.Add(definition);
            }
            return examined;
        }

        private static bool IsInfantryPiece(UnitDefinition definition)
        {
            string key = definition.jsonKey?.ToLowerInvariant();
            string name = definition.unitName?.ToLowerInvariant();
            bool matched = false;
            for (int i = 0; i < Keywords.Length; i++)
                if (Has(key, Keywords[i]) || Has(name, Keywords[i])) { matched = true; break; }
            if (!matched) return false;
            for (int i = 0; i < Excluded.Length; i++)
                if (Has(key, Excluded[i]) || Has(name, Excluded[i])) return false;
            return true;
        }

        private static bool Has(string lowered, string token) => lowered != null && lowered.Contains(token);

        private static float Footprint(UnitDefinition definition)
            => Mathf.Max(Mathf.Abs(definition.width), Mathf.Max(Mathf.Abs(definition.length), Mathf.Abs(definition.height)));

        internal void Remove()
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            for (int i = 0; i < works.Length; i++)
            {
                Scenery work = works[i];
                if (work != null && spawner != null && spawner.IsServer)
                    spawner.ServerObjectManager.Destroy(work.gameObject);
                works[i] = null;
                spawned[i] = false;
            }
            Count = 0;
        }
    }
}
