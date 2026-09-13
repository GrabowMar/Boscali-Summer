using System;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal static class RooftopPlacement
    {
        internal const string NamePrefix = ZoneGarrisonManager.NamePrefix + "Roof:";
        internal const int MaxBuildings = 96;
        internal const int MaxPerZone = 6;
        private static readonly string[] Keys = { "Emplacement1_MG", "Emplacement1_ATGM", "Emplacement1_23mm" };

        internal static BuildingDefinition ResolveDefinition(int slot)
        {
            if (Encyclopedia.i?.buildings == null) return null;
            string key = Keys[Math.Abs(slot % Keys.Length)];
            foreach (BuildingDefinition definition in Encyclopedia.i.buildings)
                if (definition != null && definition.buildingType == BuildingType.DEF &&
                    definition.unitPrefab != null && string.Equals(definition.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return definition;
            return null;
        }

        internal static bool TryPlace(GameObject shell, Bounds bounds, BuildingDefinition definition,
            out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.Euler(0f, shell.transform.eulerAngles.y, 0f);
            float halfX = Mathf.Max(4f, definition.width * 0.5f + 1.5f);
            float halfZ = Mathf.Max(4f, definition.length * 0.5f + 1.5f);
            MeshFilter[] filters = shell.GetComponentsInChildren<MeshFilter>(true);
            var probes = new System.Collections.Generic.List<Collider>(8);
            var temporary = new System.Collections.Generic.List<GameObject>(8);
            try
            {
                // Player builds discard native mesh CPU data. Reuse cooked colliders;
                // only cook a new mesh when Unity explicitly reports it readable.
                for (int i = 0; i < filters.Length && probes.Count < 8; i++)
                {
                    MeshFilter filter = filters[i];
                    if (filter.sharedMesh == null || !filter.gameObject.activeInHierarchy ||
                        filter.GetComponent<MeshRenderer>() == null) continue;
                    Vector3 scale = filter.transform.lossyScale;
                    if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f) continue;
                    var existing = filter.GetComponent<MeshCollider>();
                    if (existing != null && existing.enabled && !existing.isTrigger &&
                        existing.sharedMesh == filter.sharedMesh)
                    {
                        probes.Add(existing);
                        continue;
                    }
                    var box = filter.GetComponent<BoxCollider>();
                    if (!filter.sharedMesh.isReadable && (box == null || !box.enabled || box.isTrigger)) continue;
                    var go = new GameObject("BoscaliSummer.RoofProbe") { layer = 2 };
                    go.transform.SetParent(filter.transform, false);
                    temporary.Add(go);
                    if (filter.sharedMesh.isReadable)
                    {
                        var probe = go.AddComponent<MeshCollider>();
                        probe.sharedMesh = filter.sharedMesh;
                        probes.Add(probe);
                    }
                    else
                    {
                        // ponytail: native box-backed props have approximate roof support.
                        // Keep their footprint, but lift a short collision proxy to the
                        // visible mesh top so defenses cannot spawn inside the building.
                        var probe = go.AddComponent<BoxCollider>();
                        Bounds visible = filter.sharedMesh.bounds;
                        float bottom = box.center.y - box.size.y * .5f;
                        float top = Mathf.Max(bottom + .01f, visible.max.y);
                        probe.center = new Vector3(box.center.x, (bottom + top) * .5f, box.center.z);
                        probe.size = new Vector3(box.size.x, top - bottom, box.size.z);
                        probes.Add(probe);
                    }
                }
                if (probes.Count == 0) return false;
                // Same-frame geometry queries, including when the game is paused.
                Physics.SyncTransforms();
                float bestHeight = float.MinValue;
                float bestDistance = float.MaxValue;
                // A world-space grid avoids rotating an already rotated AABB twice.
                // Search all candidates so a podium does not win over a higher tower roof.
                for (int candidate = 0; candidate < 49; candidate++)
                {
                    int gx = candidate % 7 - 3, gz = candidate / 7 - 3;
                    Vector3 center = new Vector3(bounds.center.x + gx * bounds.extents.x * 0.26f,
                        bounds.max.y + 3f, bounds.center.z + gz * bounds.extents.z * 0.26f);
                    float low = float.MaxValue, high = float.MinValue;
                    bool valid = true;
                    for (int z = -1; z <= 1 && valid; z++)
                        for (int x = -1; x <= 1; x++)
                        {
                            var ray = new Ray(center + rotation * new Vector3(x * halfX, 0f, z * halfZ), Vector3.down);
                            bool found = false;
                            RaycastHit top = default;
                            foreach (Collider probe in probes)
                                if (probe.Raycast(ray, out RaycastHit hit, bounds.size.y + 6f) &&
                                    (!found || hit.point.y > top.point.y)) { top = hit; found = true; }
                            if (!found || top.normal.y < 0.985f || top.point.y < bounds.center.y)
                            { valid = false; break; }
                            // Reject neighbouring geometry above the selected roof, but do
                            // not let the prop's own coarse box replace the mesh surface.
                            if (Physics.Raycast(ray, out RaycastHit obstruction, top.distance - 0.05f,
                                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) &&
                                !(obstruction.transform == shell.transform || obstruction.transform.IsChildOf(shell.transform)))
                            { valid = false; break; }
                            low = Mathf.Min(low, top.point.y);
                            high = Mathf.Max(high, top.point.y);
                        }
                    float distance = gx * gx + gz * gz;
                    if (!valid || high - low > 0.25f || high < bestHeight ||
                        (Mathf.Abs(high - bestHeight) < 0.01f && distance >= bestDistance)) continue;
                    bestHeight = high;
                    bestDistance = distance;
                    position = new Vector3(center.x, high + 0.03f, center.z);
                }
                return bestHeight > float.MinValue;
            }
            finally
            {
                foreach (GameObject probe in temporary)
                {
                    // Deactivate immediately: deferred destruction must not leave a
                    // second collider active for bullets or the next physics tick.
                    if (probe != null) { probe.SetActive(false); UnityEngine.Object.Destroy(probe); }
                }
            }
        }
    }
}
