using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal static class MakeshiftFortificationBuilder
    {
        public static GameObject CreateConcreteBarrier(Vector3 position, Quaternion rotation, Transform parent)
        {
            var go = new GameObject("BoscaliSummer.ConcreteBarrier");
            go.transform.position = position;
            go.transform.rotation = rotation;
            if (parent != null) go.transform.SetParent(parent, true);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            BoxCollider col = go.AddComponent<BoxCollider>();

            Mesh mesh = new Mesh();
            mesh.name = "JerseyBarrier";

            float hw = 0.35f;
            float thw = 0.15f;
            float hl = 1.6f;
            float h = 1.1f;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-hl, 0f, -hw),
                new Vector3( hl, 0f, -hw),
                new Vector3( hl, 0f,  hw),
                new Vector3(-hl, 0f,  hw),

                new Vector3(-hl, h, -thw),
                new Vector3( hl, h, -thw),
                new Vector3( hl, h,  thw),
                new Vector3(-hl, h,  thw),
            };

            int[] triangles = new int[]
            {
                0, 4, 1,  1, 4, 5,
                2, 6, 3,  3, 6, 7,
                4, 7, 5,  5, 7, 6,
                0, 3, 4,  4, 3, 7,
                1, 5, 2,  2, 5, 6,
                0, 1, 3,  3, 1, 2
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            Material mat = MaterialProvider.GetConcreteMaterial() ?? MaterialProvider.GetSandbagMaterial();
            if (mat != null) mr.sharedMaterial = mat;

            col.center = new Vector3(0f, h * 0.5f, 0f);
            col.size = new Vector3(hl * 2f, h, hw * 2f);

            return go;
        }

        public static List<Building> DeployGroundFortifications(
            GameObject shell,
            Bounds shellBounds,
            FactionHQ owner,
            Airbase airbase,
            int slot,
            int generation,
            out List<GameObject> spawnedProps)
        {
            var spawnedUnits = new List<Building>();
            spawnedProps = new List<GameObject>();

            if (NetworkSceneSingleton<Spawner>.i == null || shell == null) return spawnedUnits;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;

            BuildingDefinition mgDef = ResolveDef("Emplacement1_MG") ?? ResolveDef("MG");
            BuildingDefinition atgmDef = ResolveDef("Emplacement1_ATGM") ?? ResolveDef("ATGM");

            Vector3 center = shellBounds.center;
            float extX = shellBounds.extents.x + 2.5f;
            float extZ = shellBounds.extents.z + 2.5f;

            Vector3[] groundOffsets = new Vector3[]
            {
                shell.transform.forward * extZ + shell.transform.right * (extX * 0.45f),
                -shell.transform.forward * extZ - shell.transform.right * (extX * 0.45f)
            };

            for (int i = 0; i < groundOffsets.Length; i++)
            {
                Vector3 probe = center + groundOffsets[i];
                probe.y = shellBounds.max.y + 10f;

                if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, shellBounds.size.y + 40f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    continue;

                if (Vector3.Angle(hit.normal, Vector3.up) > 25f) continue;
                if (hit.point.y <= Datum.LocalSeaY + 1f) continue;

                Vector3 groundPos = hit.point;
                Vector3 outward = Vector3.ProjectOnPlane(groundPos - center, Vector3.up).normalized;
                Quaternion facing = Quaternion.LookRotation(outward, Vector3.up);

                BuildingDefinition chosenDef = (i == 0 ? mgDef : atgmDef) ?? mgDef;
                if (chosenDef != null && chosenDef.unitPrefab != null)
                {
                    string uniqueName = $"BoscaliSummer:GroundDefense:{Sanitize(airbase?.name)}:{generation}:{slot}:{i}:{chosenDef.jsonKey}";
                    Building defenseUnit = spawner.SpawnBuilding(
                        chosenDef.unitPrefab,
                        groundPos.ToGlobalPosition(),
                        facing,
                        owner,
                        airbase,
                        uniqueName,
                        false,
                        null);

                    if (defenseUnit != null)
                    {
                        Renderer[] renderers = defenseUnit.GetComponentsInChildren<Renderer>(true);
                        for (int r = 0; r < renderers.Length; r++)
                            if (renderers[r] != null) renderers[r].enabled = true;

                        spawnedUnits.Add(defenseUnit);
                    }
                }

                Vector3 leftOffset = groundPos - facing * Vector3.right * 3.2f;
                Vector3 rightOffset = groundPos + facing * Vector3.right * 3.2f;

                Quaternion barrierRot = facing * Quaternion.Euler(0f, 15f, 0f);
                GameObject barrier1 = CreateConcreteBarrier(leftOffset, barrierRot, shell.transform);
                GameObject barrier2 = CreateConcreteBarrier(rightOffset, facing * Quaternion.Euler(0f, -15f, 0f), shell.transform);

                if (barrier1 != null) spawnedProps.Add(barrier1);
                if (barrier2 != null) spawnedProps.Add(barrier2);

                // Add a sentry soldier at each fortification
                GameObject sentry = VanillaSoldierFactory.CreateVisualSoldier(groundPos + facing * Vector3.forward * 2f, facing, shell.transform);
                if (sentry != null) spawnedProps.Add(sentry);
            }

            return spawnedUnits;
        }

        private static BuildingDefinition ResolveDef(string keyOrWord)
        {
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return null;
            for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
            {
                BuildingDefinition def = Encyclopedia.i.buildings[i];
                if (def != null && def.buildingType == BuildingType.DEF && def.unitPrefab != null)
                {
                    if (string.Equals(def.jsonKey, keyOrWord, StringComparison.OrdinalIgnoreCase) ||
                        (def.unitName?.IndexOf(keyOrWord, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                        return def;
                }
            }
            return null;
        }

        private static string Sanitize(string name) => (name ?? "Base").Replace(':', '_').Replace(' ', '_');
    }
}