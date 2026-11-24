using System;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Separate soot columns from surviving powerplants and hull. At sea only the impact throws spray.</summary>
    internal sealed class MegaplaneWreckSmoke : MonoBehaviour
    {
        private const float LastEmission = 200f;
        private const int Budget = 360;
        private readonly MeshFilter[] sources = new MeshFilter[3]; // Hull and a pod from each wing.
        private readonly bool[] overLand = new bool[3];
        private MegaplaneWreck wreck;
        private ParticleSystem smoke;
        private System.Random random;
        private float severity, next, nextGround;

        internal static void Add(MegaplaneWreck field)
        {
            var impact = field.Impact;
            if (GameManager.IsHeadless || impact.Age >= LastEmission || Datum.origin == null) return;
            var host = field.gameObject.AddComponent<MegaplaneWreckSmoke>();
            host.wreck = field;
            float closing = Mathf.Max(0f, -Vector3.Dot(impact.Velocity, impact.Normal));
            host.severity = Mathf.Clamp01((closing - 5f) / 57f + .44f * Mathf.InverseLerp(48f, 150f,
                Vector3.ProjectOnPlane(impact.Velocity, impact.Normal).magnitude));
            host.random = new System.Random(unchecked((int)(impact.Unit * 7919u + impact.Spawn)));
            host.smoke = MegaplaneSmokeCloud.Create(field.transform, "Groza wreck smoke", Budget, impact.Spawn);
            if (host.smoke == null) { Destroy(host); return; }
            host.ChooseSources(Quaternion.Inverse(impact.Rotation) * (impact.Contact - impact.Origin));
            host.CheckGround();
            if (impact.Age < .4f) host.ImpactCloud();
            // Carry an existing column into a late join. Warm only the last few seconds
            // at the settled source positions and never replay the initial contact burst.
            if (impact.Age > 12f)
            {
                float start = Mathf.Max(0f, impact.Age - 16f);
                for (float t = start; t < impact.Age; t += .5f)
                {
                    host.EmitColumns(t, MegaplaneSmokeCloud.Wind);
                    host.smoke.Simulate(.5f, false, false, false);
                }
                host.smoke.Play();
            }
            host.next = field.Age;
        }

        private void ChooseSources(Vector3 contact)
        {
            float[] nearest = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            foreach (MeshFilter mesh in wreck.GetComponentsInChildren<MeshFilter>(true))
            {
                string key = mesh.name;
                if (mesh.sharedMesh == null) continue;
                int slot;
                Vector3 original;
                switch (key)
                {
                    case "Fuselage_Forward": slot = 0; original = new Vector3(0f, 0f, 48f); break;
                    case "Fuselage_Center": slot = 0; original = Vector3.zero; break;
                    case "Fuselage_Aft": slot = 0; original = new Vector3(0f, 0f, -48f); break;
                    default:
                        if (!key.StartsWith("Pod_", StringComparison.Ordinal) || key.Length != 6) continue;
                        slot = key[4] == 'L' ? 1 : 2;
                        int index = key[5] - '1';
                        if (index < 0 || index > 3) continue;
                        float lateral = index == 0 ? 30f : index == 1 ? 53f : index == 2 ? 111f : 134f;
                        original = new Vector3(slot == 1 ? -lateral : lateral, -9f, 20f - index * 15f);
                        break;
                }
                float distance = (contact - original).sqrMagnitude;
                if (distance >= nearest[slot]) continue;
                nearest[slot] = distance;
                sources[slot] = mesh;
            }
        }

        private float Range(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());

        private void ImpactCloud()
        {
            var impact = wreck.Impact;
            bool water = impact.Water;
            Vector3 tangent = Vector3.ProjectOnPlane(impact.Velocity, impact.Normal);
            Vector3 forward = tangent.sqrMagnitude > .1f ? tangent.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(impact.Normal, forward);
            int count = Mathf.RoundToInt(Mathf.Lerp(10f, 58f, severity));
            for (int i = 0; i < count; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                Vector3 spread = forward * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                float plume = Range(.6f, 1.2f);
                Vector3 at = transform.position + spread * Range(1f, 11f) + impact.Normal;
                Vector3 velocity = spread * Range(5f, 20f) + tangent * Range(.02f, .15f) +
                    impact.Normal * (water ? Range(4f, 15f) : Range(1.5f, 6.5f));
                Color color = water ? new Color(.79f, .86f, .87f, .50f) : new Color(.48f, .415f, .32f, .70f);
                MegaplaneSmokeCloud.Emit(smoke, at, velocity, Mathf.Lerp(5f, 13f, severity) * plume,
                    water ? Range(1.4f, 3.8f) : Range(2.6f, 6.5f), color, Range(0f, 360f));
            }
        }

        private void CheckGround()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                MeshFilter source = sources[i];
                if (source == null || source.sharedMesh == null) continue;
                Vector3 point = source.transform.TransformPoint(source.sharedMesh.bounds.center);
                // Water contact can throw a hull section onto a beach and a dry strike can
                // toss one offshore. Check each actual source against the ground and water.
                overLand[i] = point.y > Datum.LocalSeaY + 100f ||
                    Physics.Raycast(point + Vector3.up * 140f, Vector3.down, out RaycastHit hit, 265f,
                        PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) &&
                    !MegaplaneCrash.IsWater(hit.collider) && hit.point.y > Datum.LocalSeaY + .05f;
            }
        }

        private void Update()
        {
            float age = wreck.Age;
            if (age >= LastEmission)
            {
                smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                enabled = false;
                return;
            }
            if (age >= nextGround)
            {
                CheckGround();
                nextGround = wreck.Settled ? LastEmission : age + .70f;
                // Once all wet pieces have sunk, the short-lived spray can die by itself.
                if (wreck.Settled && !overLand[0] && !overLand[1] && !overLand[2])
                {
                    smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    enabled = false;
                    return;
                }
            }
            if (age < next) return;
            float heat = Heat(age);
            next = age + Mathf.Lerp(.77f, .18f, heat);
            EmitColumns(age, MegaplaneSmokeCloud.Wind);
        }

        private float Heat(float age) => Mathf.Lerp(.22f, 1f, severity) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, LastEmission, age)));

        private void EmitColumns(float age, Vector3 wind)
        {
            float heat = Heat(age);
            for (int i = 0; i < sources.Length; i++)
            {
                if (!overLand[i] || sources[i] == null || heat < .32f && i == 2) continue;
                var source = sources[i];
                Bounds bounds = source.sharedMesh.bounds;
                Vector3 center = source.transform.TransformPoint(bounds.center);
                if (center.y < Datum.LocalSeaY - .25f) continue;
                float scale = i == 0 ? 1.15f : .79f;
                float phase = age * .72f + i * 1.8f;
                Vector3 eddy = new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase * .87f));
                Vector3 at = center + Vector3.up * Mathf.Min(8f, bounds.extents.y * .7f) + eddy * scale * 2.2f;
                Vector3 velocity = wind * Range(.74f, 1.12f) + eddy * Range(1.1f, 3.4f) +
                    Vector3.up * Mathf.Lerp(2.2f, Range(8f, 13f), heat) * scale;
                float shade = Mathf.Lerp(.51f, Range(.17f, .34f), heat);
                var color = new Color(shade * 1.04f, shade, shade * .93f, Mathf.Lerp(.20f, .89f, heat));
                MegaplaneSmokeCloud.Emit(smoke, at, velocity, Mathf.Lerp(5f, 16f, heat) * scale * Range(.82f, 1.19f),
                    Mathf.Lerp(9f, Range(14f, 21f), heat), color, Range(-150f, 150f));
            }
        }
    }
}
