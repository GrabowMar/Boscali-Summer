using System.Collections.Generic;
using BoscaliSummer.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Pooled vanilla soot decals stamped on the ground where a fire burned. Client-local
    /// cosmetic: no colliders, no networking, and the oldest mark is recycled at the ceiling
    /// so a long campaign cannot grow the projector count without bound.
    /// </summary>
    internal sealed class BurnScarPool
    {
        private const int MaximumScars = 64;
        private const int SeedSalt = 0x4b1d5c07;

        private readonly List<GameObject> marks = new List<GameObject>(MaximumScars);
        private int ringHead;
        private bool warnedUnavailable;

        public void Stamp(GlobalPosition position, float diameter)
        {
            if (GameManager.IsHeadless || diameter <= 0f) return;
            // The same cached ground probe the fire front uses: one terrain cast per 24 m
            // cell instead of one per decal lobe.
            if (!TerrainProbeCache.TryProbe(position, out GlobalPosition ground, out Vector3 normal)) return;

            GameObject mark = Acquire();
            if (mark == null) return;
            DecalProjector projector = mark.GetComponent<DecalProjector>();
            if (projector == null) return;

            // Ground projection: the box looks along the surface normal, rolled around it so
            // neighbouring scars never read as identical stamps.
            Vector3 forward = -normal;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            uint seed = Deterministic.Hash(
                Mathf.RoundToInt(ground.x), Mathf.RoundToInt(ground.y),
                Mathf.RoundToInt(ground.z), SeedSalt);
            float roll = Deterministic.UnitFloat(seed) * 360f;
            mark.transform.rotation =
                Quaternion.AngleAxis(roll, forward) * Quaternion.LookRotation(forward, up);
            mark.transform.position = ground.ToLocalPosition();

            float depth = Mathf.Clamp(diameter * 0.18f, 1.6f, 6f);
            projector.size = new Vector3(diameter, diameter, depth);
            projector.renderingLayerMask = ~0u;
            projector.startAngleFade = 45f;
            projector.endAngleFade = 70f;
            projector.fadeFactor = 1f;
            projector.drawDistance = 4000f;

            Material scorch = ScorchDecalMaterialResolver.Resolve();
            if (scorch != null) projector.material = scorch;
        }

        public void Clear()
        {
            for (int i = 0; i < marks.Count; i++)
                if (marks[i] != null) Object.Destroy(marks[i]);
            marks.Clear();
            ringHead = 0;
            warnedUnavailable = false;
            ScorchDecalMaterialResolver.ResetForScene();
        }

        private GameObject Acquire()
        {
            if (marks.Count < MaximumScars)
            {
                GameObject prefab = GameAssets.i != null ? GameAssets.i.scorchMarkDecal : null;
                if (prefab == null)
                {
                    if (!warnedUnavailable)
                    {
                        warnedUnavailable = true;
                        Plugin.Logger.LogWarning(
                            "Ground burn scars unavailable: GameAssets.scorchMarkDecal is not loaded.");
                    }
                    return null;
                }
                GameObject created = Object.Instantiate(prefab, Datum.origin, false);
                created.name = "BoscaliSummer.BurnScar";
                created.SetActive(true);
                marks.Add(created);
                return created;
            }
            GameObject oldest = marks[ringHead];
            ringHead = (ringHead + 1) % marks.Count;
            if (oldest != null) oldest.SetActive(true);
            return oldest;
        }
    }
}
