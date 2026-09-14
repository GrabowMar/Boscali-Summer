using System;
using System.Collections.Generic;
using UnityEngine;
using BoscaliSummer.Features.Trenches.Domain;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    // Native buildings own targeting, ammunition, damage, rewards and Mirage replication.
    internal sealed class TrenchGarrison
    {
        internal const string Prefix = "BoscaliSummer:Trench:";
        internal const int MaximumDefenders = 4;
        private static readonly string[] Keys = { "Emplacement1_MG", "Emplacement1_ATGM", "Emplacement1_MANPADS" };
        private static readonly int[] SlotKind = { 0, 0, 1, 2 }; // Two MG teams, one ATGM, one MANPADS
        private readonly Building[] defenders = new Building[MaximumDefenders];
        private readonly UnitPart[][] parts = new UnitPart[MaximumDefenders][];
        private readonly float[] previousHealth = new float[MaximumDefenders];
        private readonly bool[] committed = new bool[MaximumDefenders];
        private readonly int[] attempts = new int[MaximumDefenders];
        private readonly BuildingDefinition[] definitions = new BuildingDefinition[3];
        private readonly TrenchNetwork network;
        internal int Alive { get; private set; }
        internal bool Overrun { get; private set; }
        internal float SuppressedUntil { get; private set; }

        internal TrenchGarrison(TrenchNetwork net)
        {
            network = net;
            var catalog = Encyclopedia.i?.buildings;
            for (int i = 0; catalog != null && i < Math.Min(catalog.Count, 512); i++)
            {
                var def = catalog[i];
                if (def == null || def.unitPrefab == null || def.buildingType != BuildingType.DEF) continue;
                for (int kind = 0; kind < Keys.Length; kind++)
                    if (string.Equals(def.jsonKey, Keys[kind], StringComparison.OrdinalIgnoreCase)) definitions[kind] = def;
            }
        }

        internal bool Establish()
        {
            try
            {
                if (!Spawn(0) || !Spawn(1)) { Remove(); return false; }
                Alive = 2;
                return true;
            }
            catch { Remove(); throw; }
        }

        internal void Reinforce()
        {
            if (Overrun) return;
            int desired = TrenchTacticalMath.DefenderBudget((int)network.Stage);
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
                if (unit == null || unit.disabled || unit.NetworkHQ != network.OwnerHq)
                {
                    if (previousHealth[slot] >= 0)
                    {
                        previousHealth[slot] = -1;
                        SuppressedUntil = now + TrenchTacticalMath.ConstructionSuppressionSeconds;
                        changed = true;
                    }
                    continue;
                }
                alive++;
                float health = Health(parts[slot]);
                if (health < previousHealth[slot] - 0.01f)
                {
                    SuppressedUntil = now + TrenchTacticalMath.ConstructionSuppressionSeconds;
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
            if (spawner == null || !spawner.IsServer || network.OwnerHq == null) return false;
            attempts[slot]++;
            var def = definitions[SlotKind[slot]];
            if (def == null) return false;
            Vector3 forward = network.ThreatDirection;
            // Sparse but spread: MG teams on the front flanks, the ATGM on the fire-line
            // centre, and the MANPADS back at the support line watching the air.
            float span = Math.Max(60f, network.FrontHalfSpan);
            float lateral, depth;
            switch (slot)
            {
                case 0: lateral = -0.62f * span; depth = -16f; break;
                case 1: lateral = 0.62f * span; depth = -16f; break;
                case 2: lateral = 0f; depth = -16f; break;
                default: lateral = 0f; depth = -TrenchTacticalMath.SupportLineDepth - 8f; break;
            }
            Vector3 desired = network.SeedCenter + network.LateralAxis * lateral + forward * depth;
            Quaternion rotation = Quaternion.LookRotation(forward);
            Vector3 offset = rotation * def.spawnOffset;
            desired += new Vector3(offset.x, 0, offset.z);
            if (!TrenchPlacement.TryGround(desired, out Vector3 ground)) return false;
            // Validate the actual native emplacement footprint, not just a point.
            float halfWidth = Math.Max(2f, def.width * 0.5f + 1f);
            float halfLength = Math.Max(2f, def.length * 0.5f + 1f);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 p = ground + rotation * new Vector3((corner & 1) == 0 ? -halfWidth : halfWidth, 0,
                    (corner & 2) == 0 ? -halfLength : halfLength);
                if (network.PlacementValidator != null && !network.PlacementValidator(p)) return false;
                if (!TrenchPlacement.TryGround(p, out Vector3 sample) || Math.Abs(sample.y - ground.y) > 1f) return false;
            }
            Vector3 half = new Vector3(halfWidth, Math.Max(1f, def.height * 0.5f), halfLength);
            Vector3 volume = new GlobalPosition(ground).ToLocalPosition() + Vector3.up * (half.y + 0.2f);
            if (Physics.CheckBox(volume, half, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            Building unit = spawner.SpawnBuilding(def.unitPrefab, new GlobalPosition(ground + Vector3.up * offset.y), rotation,
                network.OwnerHq, null, Prefix + network.Id + ":" + slot, false, null);
            if (unit == null) return false;
            defenders[slot] = unit;
            committed[slot] = true; // A destroyed slot never respawns or grants farmable repeat rewards.
            parts[slot] = unit.GetComponentsInChildren<UnitPart>();
            previousHealth[slot] = Health(parts[slot]);
            return true;
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
                if (unit != null && unit.NetworkHQ == network.OwnerHq && spawner != null && spawner.IsServer)
                    spawner.ServerObjectManager.Destroy(unit.gameObject);
                defenders[i] = null;
            }
        }
    }
}
