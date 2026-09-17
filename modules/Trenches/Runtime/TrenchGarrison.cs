using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    // Native buildings own targeting, ammunition, damage, rewards and Mirage replication.
    internal sealed class TrenchGarrison
    {
        internal const string Prefix = "BoscaliSummer:Trench:";
        internal const int MaximumDefenders = 4;
        // Behind the parados and clear of the earthwork's rear skirt, which reaches about
        // five metres behind the ditch centreline, so the sandbag ring sits against the
        // reverse slope instead of spanning the cut.
        private const float NestRearOffset = 7.5f;
        // One OverlapBox query per attempt is enough for a lone emplacement; a saturated
        // buffer is treated as blocked so a crowded site is never accepted half-tested.
        private const int ObstacleCeiling = 16;
        private static readonly string[] Keys = { "Emplacement1_MG", "Emplacement1_ATGM", "Emplacement1_MANPADS" };
        private static readonly int[] SlotKind = { 0, 0, 1, 2 }; // Two MG teams, one ATGM, one MANPADS
        private static readonly bool[] missingReported = new bool[Keys.Length]; // Once per process
        // Preferred station along the line, and the spread of fallbacks tried when a bay's
        // native footprint is blocked: one bad emplacement must not cost a whole position.
        private static readonly float[] FireFractions = { 0.15f, 0.85f, 0.5f };
        private static readonly float[] FallbackSpread = { 0f, 0.2f, -0.2f };
        private readonly Collider[] obstacles = new Collider[ObstacleCeiling];
        private readonly Building[] defenders = new Building[MaximumDefenders];
        private readonly UnitPart[][] parts = new UnitPart[MaximumDefenders][];
        private readonly float[] previousHealth = new float[MaximumDefenders];
        private readonly bool[] committed = new bool[MaximumDefenders];
        private readonly int[] attempts = new int[MaximumDefenders];
        private readonly BuildingDefinition[] definitions = new BuildingDefinition[3];
        private readonly TrenchLine line;
        internal int Alive { get; private set; }
        internal bool Overrun { get; private set; }
        internal float SuppressedUntil { get; private set; }
        // Why the last placement failed, from a fixed short list, so a rejected position can
        // name the check that refused it instead of leaving the generic warning unexplained.
        internal string LastFailure { get; private set; } = "not attempted";

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
            for (int slot = 0; slot < desired; slot++)
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

        private bool Spawn(int slot)
        {
            var spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) { LastFailure = "no server spawner"; return false; }
            if (line.OwnerHq == null) { LastFailure = "no owner HQ"; return false; }
            attempts[slot]++;
            var def = definitions[SlotKind[slot]];
            if (def == null) { LastFailure = "missing native definition " + Keys[SlotKind[slot]]; return false; }
            for (int attempt = 0; attempt < FallbackSpread.Length; attempt++)
            {
                Vector3? anchor = FindAnchor(slot, attempt);
                if (!anchor.HasValue) { LastFailure = "no anchor"; return false; }
                if (TrySpawnAt(slot, def, anchor.Value)) return true;
            }
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
            Vector3 desired = anchor - forward * NestRearOffset + new Vector3(offset.x, 0, offset.z);
            if (!TrenchTerrain.TryGround(desired, out Vector3 ground)) { LastFailure = "no ground"; return false; }
            // Validate the actual native emplacement footprint, not just a point.
            float halfWidth = Math.Max(2f, def.width * 0.5f + 1f);
            float halfLength = Math.Max(2f, def.length * 0.5f + 1f);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 p = ground + rotation * new Vector3((corner & 1) == 0 ? -halfWidth : halfWidth, 0,
                    (corner & 2) == 0 ? -halfLength : halfLength);
                if (!line.Contains(p)) { LastFailure = "outside the position"; return false; }
                if (!TrenchTerrain.TryGround(p, out Vector3 sample) || Math.Abs(sample.y - ground.y) > 1f)
                { LastFailure = "uneven ground"; return false; }
            }
            Vector3 half = new Vector3(halfWidth, Math.Max(1f, def.height * 0.5f), halfLength);
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
        /// The station a defender occupies: spread along the fire line for the ground teams,
        /// the support centre for the air watch. Zero until that line has been dug, so a
        /// slot waits for the belt to grow instead of standing in an empty field.
        /// </summary>
        private Vector3? FindAnchor(int slot, int attempt)
        {
            float spread = FallbackSpread[attempt % FallbackSpread.Length];
            if (slot >= 3) return Pick(line.SupportAnchors, Mathf.Clamp01(0.5f + spread * 0.6f));
            return Pick(line.Anchors, Mathf.Clamp01(FireFractions[slot] + spread));
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
