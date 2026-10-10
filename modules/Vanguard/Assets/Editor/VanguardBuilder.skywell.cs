// SKYWELL (pit stop) checks for the Unity batch: the runtime joint API, the body frame the runtime derives from the model, the
// lamp children SkywellGlow tints, the stowed envelope and the triangle budget. Called from VanguardBuilder.ValidateAndPreview.
using System;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // Runtime joint API (Domain/SkywellTender.cs SkywellRig, Presentation/SkywellVisuals.cs; model: Source/models/SkywellKit.py).
        // The runtime skips a joint it cannot find, so a stale or half-updated FBX would fly silently wrong: every name is required.
        static readonly string[] SkywellJoints =
        {
            "Deck", "Winch", "Tender", "TetherPoint", "WingL", "WingR", "Nacelle0", "Nacelle1", "Nacelle2", "Nacelle3", "Fan0", "Fan1", "Fan2", "Fan3",
            "FuelBase", "FuelBoom", "FuelTele", "Nozzle", "LoadBase", "LoadUpper", "LoadFore", "Gripper", "Magazine",
            "Round0", "Round1", "Round2", "Round3", "Round4", "Round5",
        };

        // Joints whose lamps SkywellGlow tints: each needs a DIRECT child named Glow* (the LOD meshes hang under it).
        static readonly string[] SkywellGlowJoints = { "Deck", "Tender", "Nozzle", "Gripper", "Fan0", "Fan1", "Fan2", "Fan3" };

        // Stowed bounding box, sorted ascending (m): the built 1.72 x 2.24 x 6.83 box plus a small allowance per axis.
        static readonly float[] SkywellEnvelope = { 1.80f, 2.30f, 6.90f };

        const int SkywellMaxTriangles = 15000;

        static void ValidateSkywellKit(GameObject visual)
        {
            var joints = visual.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            string[] lost = SkywellJoints.Where(n => !joints.ContainsKey(n)).ToArray();
            if (lost.Length > 0) throw new Exception("Skywell lost joint(s) " + string.Join(",", lost));
            string[] noLamps = SkywellGlowJoints.Where(n => !joints[n].Cast<Transform>().Any(c => c.name.StartsWith("Glow", StringComparison.Ordinal))).ToArray();
            if (noLamps.Length > 0) throw new Exception("SKYWELL_GLOW_MISSING " + string.Join(",", noLamps) + ": those lamps will not change colour");

            // The runtime takes the tender's body frame from the model: forward = Tender pivot -> TetherPoint, up = toward the dorsal
            // joints. A pivot off the nose line pitches or yaws the whole tender in flight, so both must share the centreline and height
            // (the mount frame here: nose +Z, up +Y).
            Transform mount = visual.transform.parent, tender = joints["Tender"];
            Vector3 nose = mount.InverseTransformVector(joints["TetherPoint"].position - tender.position);
            if (nose.z < 1f || Mathf.Abs(nose.x) > 0.01f || Mathf.Abs(nose.y) > 0.01f)
                throw new Exception("Skywell Tender pivot and TetherPoint must share the centreline and height, got " + nose.ToString("F3"));
            Vector3 dorsal = mount.InverseTransformVector((joints["FuelBase"].position + joints["LoadBase"].position + joints["Magazine"].position) / 3f - tender.position);
            if (dorsal.y < 0.1f || Mathf.Abs(dorsal.x) > 0.01f) throw new Exception("Skywell dorsal joints must sit above the Tender pivot, symmetric, got " + dorsal.ToString("F3"));
            if (mount.InverseTransformPoint(joints["WingL"].position).x >= 0f || mount.InverseTransformPoint(joints["WingR"].position).x <= 0f)
                throw new Exception("Skywell wing pivot handedness");

            Vector3 size = VisualBounds(visual).size;
            float[] sorted = { size.x, size.y, size.z };
            Array.Sort(sorted);
            for (int i = 0; i < sorted.Length; i++)
                if (sorted[i] > SkywellEnvelope[i])
                    throw new Exception("Skywell stowed box " + size + " exceeds the cargo-bay envelope " + string.Join(" x ", SkywellEnvelope));

            int triangles = visual.GetComponentsInChildren<MeshRenderer>(true).Where(r => LodLevel(r.name) == 0)
                .Sum(r => (int)r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
            if (triangles > SkywellMaxTriangles) throw new Exception("Skywell LOD0 has " + triangles + " triangles, budget " + SkywellMaxTriangles);
            Debug.Log("[Vanguard] SKYWELL_MODEL_OK joints=" + SkywellJoints.Length + " nose=" + nose.z.ToString("F2") + " box=" + size + " tris=" + triangles);
        }
    }
}
