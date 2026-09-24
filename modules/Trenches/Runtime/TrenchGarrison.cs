using System;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    // Native buildings own targeting, ammunition, damage, rewards and Mirage replication.
    internal sealed class TrenchGarrison
    {
        internal const string Prefix = "BoscaliSummer:Trench:";
        internal const int MaximumDefenders = 8;
        // Nests stand in the ditch on their bay node, just forward of the centreline, so the
        // weapon sits on the crest of the cut at the bay the ditch flares for it - not on the
        // reverse slope 7.5m behind it, which is where the (now hidden) sandbag ring sat.
        private const float NestForwardOffset = 0.4f;
        // One OverlapBox query per attempt is enough for a lone emplacement; a saturated
        // buffer is treated as blocked so a crowded site is never accepted half-tested.
        private const int ObstacleCeiling = 16;
        // Probe half-extent when the prefab cannot answer.
        private const float FootprintFallback = 1.6f;
        private static readonly string[] Keys = { "Emplacement1_MG", "Emplacement1_ATGM", "Emplacement1_MANPADS", "Emplacement1_23mm" };
        // Four MG teams, one ATGM road-watch, one MANPADS air watch and two 23mm guns: early
        // budgets stay MG-heavy and the heavier teams only arrive with the belt.
        private static readonly int[] SlotKind = { 0, 0, 0, 0, 1, 2, 3, 3 };
        private static readonly bool[] missingReported = new bool[Keys.Length]; // Once per process
        // The preferred bay node, then its neighbours, tried when a footprint is blocked:
        // one bad emplacement must not cost a whole position. Steps stay inside the node
        // array and never revisit a node.
        private static readonly int[] NodeSteps = { 0, 1, -1, 2, -2 };
        // Fraction spread over the anchors, used only when the curve produced no bays.
        private static readonly float[] AnchorSpread = { 0f, 0.1f, -0.1f, 0.2f, -0.2f };
        private readonly float[] probeHalfExtents = new float[Keys.Length];
        private readonly Collider[] obstacles = new Collider[ObstacleCeiling];
        private readonly Building[] defenders = new Building[MaximumDefenders];
        private readonly UnitPart[][] parts = new UnitPart[MaximumDefenders][];
        private readonly float[] previousHealth = new float[MaximumDefenders];
        private readonly bool[] committed = new bool[MaximumDefenders];
        private readonly int[] attempts = new int[MaximumDefenders];
        private readonly BuildingDefinition[] definitions = new BuildingDefinition[Keys.Length];
        private readonly TrenchLine line;
        internal int Alive { get; private set; }
        internal bool Overrun { get; private set; }
        internal float SuppressedUntil { get; private set; }
        // Why the last placement failed, from a fixed short list, so a rejected position can
        // name the check that refused it instead of leaving the generic warning unexplained.
        internal string LastFailure { get; private set; } = "not attempted";
        // Distance to the nearest road surface in metres, NaN where unknown. Set by the
        // manager when the road index is ready; null keeps the plain fraction spread.
        internal Func<float, float, float> RoadDistanceAt { get; set; }

        internal TrenchGarrison(TrenchLine position)
        {
            line = position;
            var catalog = Encyclopedia.i?.buildings;
            for (int i = 0; catalog != null && i < Math.Min(catalog.Count, 512); i++)
            {
                var def = catalog[i];
                if (def == null || def.unitPrefab == null || def.buildingType != BuildingType.DEF) continue;
                for (int kind = 0; kind < Keys.Length; kind++)
                    if (string.Equals(def.jsonKey, Keys[kind], StringComparison.OrdinalIgnoreCase)) definitions[kind] = def;
            }
            for (int kind = 0; kind < Keys.Length; kind++)
                if (definitions[kind] == null && !missingReported[kind])
                {
                    missingReported[kind] = true;
                    Debug.LogWarning("[TrenchGarrison] Native defense definition missing: " + Keys[kind] + ".");
                }
            for (int kind = 0; kind < Keys.Length; kind++)
                Measure(definitions[kind], out probeHalfExtents[kind]);
        }

        /// <summary>
        /// The emplacement's real weapon footprint, read from its prefab's root BoxCollider.
        /// The definition's width/length describe the sandbag ring, which is hidden on every
        /// peer, so they are no longer used: a probe built from them spans the whole bay
        /// (works sit 2.6m behind the line, neighbouring nests ~20m apart in the same cut).
        /// </summary>
        private static void Measure(BuildingDefinition definition, out float halfExtent)
        {
            halfExtent = FootprintFallback;
            BoxCollider box = definition?.unitPrefab != null ? definition.unitPrefab.GetComponent<BoxCollider>() : null;
            if (box == null) return;
            float footprint = Mathf.Max(Mathf.Abs(box.size.x), Mathf.Abs(box.size.z));
            if (!(footprint > 0.01f)) return;
            halfExtent = Mathf.Clamp(footprint * 0.5f + 0.25f, 1.2f, 2.0f);
        }

        internal bool Establish()
        {
            try
            {
                // One blocked nest must not cost the position: establish on the teams that
                // actually spawned, and reject only when none did.
                int spawned = 0;
                for (int slot = 0; slot < 2; slot++)
                    if (Spawn(slot)) spawned++;
                if (spawned == 0) { Remove(); return false; }
                Alive = spawned;
                return true;
            }
            catch { Remove(); throw; }
        }

        internal void Reinforce()
        {
            if (Overrun) return;
            int desired = TrenchTraceMath.DefenderBudget(line.Stage);
            for (int slot = 0; slot < desired && slot < defenders.Length; slot++)
                if (!committed[slot] && attempts[slot] < 3) Spawn(slot);
        }

        internal bool Poll(float now)
        {
            bool changed = false;
            int alive = 0;
            for (int slot = 0; slot < defenders.Length; slot++)
            {
                if (!committed[slot]) continue;
                Building unit = defenders[slot];
                if (unit == null || unit.disabled || unit.NetworkHQ != line.OwnerHq)
                {
                    if (previousHealth[slot] >= 0)
                    {
                        previousHealth[slot] = -1;
                        SuppressedUntil = now + TrenchTraceMath.ConstructionSuppressionSeconds;
                        changed = true;
                    }
                    continue;
                }
                alive++;
                float health = Health(parts[slot]);
                if (health < previousHealth[slot] - 0.01f)
                {
                    SuppressedUntil = now + TrenchTraceMath.ConstructionSuppressionSeconds;
                    changed = true;
                }
                previousHealth[slot] = health;
            }
            if (alive != Alive) { Alive = alive; changed = true; }
            if (Alive == 0 && !Overrun) { Overrun = true; changed = true; }
            return changed;
        }

        internal bool ObservedBy(FactionHQ observer)
        {
            if (observer == null) return false;
            for (int i = 0; i < defenders.Length; i++)
            {
                Building unit = defenders[i];
                if (unit != null && !unit.disabled && observer.IsTargetBeingTracked(unit)) return true;
            }
            return false;
        }

        private bool Spawn(int slot)
        {
            var spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) { LastFailure = "no server spawner"; return false; }
            if (line.OwnerHq == null) { LastFailure = "no owner HQ"; return false; }
            attempts[slot]++;
            var def = definitions[SlotKind[slot]];
            if (def == null) { LastFailure = "missing native definition " + Keys[SlotKind[slot]]; return false; }
            int budget = TrenchTraceMath.DefenderBudget(line.Stage);
            bool anchored = false;
            for (int attempt = 0; attempt < NodeSteps.Length; attempt++)
            {
                Vector3? anchor = FindAnchor(slot, budget, attempt);
                if (!anchor.HasValue) continue;
                anchored = true;
                if (TrySpawnAt(slot, def, anchor.Value)) return true;
            }
            if (!anchored) LastFailure = "no anchor";
            return false;
        }

        /// <summary>
        /// Validates the native emplacement footprint at one station and hands the spawn to
        /// vanilla, which keeps targeting, damage, rewards and Mirage replication.
        /// </summary>
        private bool TrySpawnAt(int slot, BuildingDefinition def, Vector3 anchor)
        {
            var spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) { LastFailure = "no server spawner"; return false; }
            Vector3 forward = line.ThreatAt(anchor);
            Quaternion rotation = Quaternion.LookRotation(forward);
            Vector3 offset = rotation * def.spawnOffset;
            Vector3 desired = anchor + forward * NestForwardOffset + new Vector3(offset.x, 0, offset.z);
            if (!TrenchTerrain.TryGround(desired, out Vector3 ground)) { LastFailure = "no ground"; return false; }
            // Validate the weapon's own footprint (the prefab's root BoxCollider), never
            // def.width/length: those describe the hidden sandbag ring and would cover the bay.
            float halfExtent = probeHalfExtents[SlotKind[slot]];
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 p = ground + rotation * new Vector3((corner & 1) == 0 ? -halfExtent : halfExtent, 0,
                    (corner & 2) == 0 ? -halfExtent : halfExtent);
                if (!line.Contains(p)) { LastFailure = "outside the position"; return false; }
                if (!TrenchTerrain.TryGround(p, out Vector3 sample) || Math.Abs(sample.y - ground.y) > 1f)
                { LastFailure = "uneven ground"; return false; }
            }
            Vector3 half = new Vector3(halfExtent, Math.Max(1f, def.height * 0.5f), halfExtent);
            Vector3 volume = new GlobalPosition(ground).ToLocalPosition() + Vector3.up * (half.y + 0.2f);
            if (Blocked(volume, half, rotation)) { LastFailure = "footprint blocked"; return false; }
            Building unit = spawner.SpawnBuilding(def.unitPrefab, new GlobalPosition(ground + Vector3.up * offset.y), rotation,
                line.OwnerHq, null, Prefix + line.Id + ":" + slot, false, null);
            if (unit == null) { LastFailure = "spawn returned null"; return false; }
            defenders[slot] = unit;
            committed[slot] = true; // A destroyed slot never respawns or grants farmable repeat rewards.
            parts[slot] = unit.GetComponentsInChildren<UnitPart>();
            previousHealth[slot] = Health(parts[slot]);
            return true;
        }

        /// <summary>
        /// Overlap probe for the actual emplacement volume. Terrain is the ground the nest
        /// stands on, not an obstacle: on any real slope the box clips it, so terrain colliders
        /// are identified exactly as <see cref="TrenchTerrain.TryGround"/> does and skipped.
        /// The buffer is a hard ceiling, and a saturated query fails closed.
        /// </summary>
        private bool Blocked(Vector3 volume, Vector3 half, Quaternion rotation)
        {
            int count = Physics.OverlapBoxNonAlloc(volume, half, obstacles, rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = obstacles[i];
                if (collider == null) continue;
                if (GameAssets.i?.terrainMaterial != null &&
                    collider.sharedMaterial == GameAssets.i.terrainMaterial) continue;
                return true;
            }
            return count >= obstacles.Length;
        }

        /// <summary>
        /// The bay node a nest occupies: the slot's fraction of the fire-curve node schedule,
        /// then the neighbouring nodes of <see cref="NodeSteps"/> so a blocked bay still finds
        /// ground. Falls back to the anchors with a small spread when the curve produced no
        /// nodes, so spawning never silently stops.
        /// </summary>
        private Vector3? FindAnchor(int slot, int budget, int attempt)
        {
            float fraction = TrenchTraceMath.NodeFraction(slot, budget);
            Vector3[] nodes = line.Nodes;
            if (nodes != null && nodes.Length > 0)
            {
                // The anti-tank team watches the nearest road inside the watch distance, so a
                // road crossing the position gets covered. A blocked bay still falls back to
                // the neighbouring nodes like every other slot.
                if (attempt == 0 && RoadDistanceAt != null && SlotKind[slot] == 1 &&
                    TryRoadWatchNode(nodes, out Vector3 watched)) return watched;
                int baseIndex = Mathf.Clamp(Mathf.RoundToInt(fraction * (nodes.Length - 1)), 0, nodes.Length - 1);
                int index = baseIndex + NodeSteps[attempt];
                if (index < 0 || index >= nodes.Length) return null; // stay inside, never repeat a node
                return nodes[index];
            }
            return Pick(line.Anchors, Mathf.Clamp01(fraction + AnchorSpread[attempt]));
        }

        /// <summary>Bay node nearest a road inside the watch distance, for the ATGM team.</summary>
        private bool TryRoadWatchNode(Vector3[] nodes, out Vector3 watched)
        {
            watched = default;
            float best = TrenchTraceMath.RoadWatchDistance;
            bool found = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                float distance = RoadDistanceAt(nodes[i].x, nodes[i].z);
                if (float.IsNaN(distance) || distance >= best) continue;
                best = distance;
                watched = nodes[i];
                found = true;
            }
            return found;
        }

        private static Vector3? Pick(Vector3[] anchors, float fraction)
        {
            if (anchors == null || anchors.Length == 0) return null;
            return anchors[Mathf.Clamp(Mathf.RoundToInt(fraction * (anchors.Length - 1)), 0, anchors.Length - 1)];
        }

        private static float Health(UnitPart[] list)
        {
            float sum = 0;
            for (int i = 0; list != null && i < Math.Min(list.Length, 32); i++)
                if (list[i] != null) sum += Math.Max(0, list[i].hitPoints);
            return sum;
        }

        internal void Remove()
        {
            var spawner = NetworkSceneSingleton<Spawner>.i;
            for (int i = 0; i < defenders.Length; i++)
            {
                var unit = defenders[i];
                if (unit != null && unit.NetworkHQ == line.OwnerHq && spawner != null && spawner.IsServer)
                    spawner.ServerObjectManager.Destroy(unit.gameObject);
                defenders[i] = null;
            }
        }
    }
}
