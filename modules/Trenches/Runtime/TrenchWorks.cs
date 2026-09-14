using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Places small infantry-scale game scenery along a sector's ditches — HESCO, sandbag
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

        private static readonly string[] Keywords = { "hesco", "sandbag", "gabion", "dugout" };
        private static readonly string[] Excluded = { "hulldown", "shelter", "concrete", "camonet", "wall" };

        private static List<UnitDefinition> catalog;
        private static Dictionary<string, UnitDefinition> catalogSource;
        private static bool catalogReported;

        private readonly Scenery[] works = new Scenery[MaximumWorks];
        private readonly bool[] spawned = new bool[MaximumWorks];
        private readonly List<TrenchNode> line = new List<TrenchNode>(48);
        private readonly TrenchNetwork network;

        internal int Count { get; private set; }

        internal TrenchWorks(TrenchNetwork net)
        {
            network = net;
        }

        /// <summary>Adds the works unlocked by <paramref name="stage"/>; safe to call repeatedly.</summary>
        internal void Deploy(TrenchStage stage)
        {
            if (network == null || network.Overrun) return;
            int target = stage >= TrenchStage.Stage4_Integrated ? MaximumWorks
                : stage >= TrenchStage.Stage3_Hardened ? 4 : 0;
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
            TrenchNode anchor = line[Mathf.Clamp((rank + 1) * line.Count / 5, 0, line.Count - 1)];
            Vector3 desired = anchor.Position + network.ThreatDirection * 0.8f; // on the parapet line
            Vector3 ground = TrenchPlacement.TryGround(desired, out Vector3 sampled) ? sampled : anchor.Position;

            UnitDefinition piece = pieces[index % pieces.Count];
            Scenery scenery = spawner.SpawnScenery(piece.unitPrefab, new GlobalPosition(ground), anchor.Rotation,
                Prefix + network.Id + ":" + index);
            if (scenery == null) return false;
            works[index] = scenery;
            Count++;
            return true;
        }

        private void CollectLine(bool support)
        {
            line.Clear();
            float target = support ? -TrenchTacticalMath.SupportLineDepth : 0f;
            foreach (var node in network.Nodes)
            {
                float forward = Vector3.Dot(node.Position - network.SeedCenter, network.ThreatDirection);
                if (Mathf.Abs(forward - target) > 16f) continue;
                line.Add(node);
            }
            line.Sort((a, b) => Lateral(a).CompareTo(Lateral(b)));
        }

        private float Lateral(TrenchNode node)
            => Vector3.Dot(node.Position - network.SeedCenter, network.LateralAxis);

        private static List<UnitDefinition> ResolveCatalog()
        {
            var lookup = Encyclopedia.Lookup;
            if (lookup == null) return null;
            if (catalog != null && ReferenceEquals(catalogSource, lookup)) return catalog;

            catalogSource = lookup;
            catalog = new List<UnitDefinition>(CatalogCeiling);
            int examined = 0;
            foreach (var pair in lookup)
            {
                if (++examined > 4096) break;
                UnitDefinition definition = pair.Value;
                if (definition?.unitPrefab == null || definition.unitPrefab.GetComponent<Scenery>() == null) continue;
                if (!IsInfantryPiece(pair.Key) || Footprint(definition) > MaximumFootprint) continue;
                catalog.Add(definition);
            }
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
            }
            return catalog;
        }

        private static bool IsInfantryPiece(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string lower = key.ToLowerInvariant();
            bool matched = false;
            for (int i = 0; i < Keywords.Length; i++)
                if (lower.Contains(Keywords[i])) { matched = true; break; }
            if (!matched) return false;
            for (int i = 0; i < Excluded.Length; i++)
                if (lower.Contains(Excluded[i])) return false;
            return true;
        }

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
