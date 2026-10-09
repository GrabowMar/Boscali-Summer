// SKYWELL (pit stop) checks for the Unity batch: the runtime joint API, the additive pit-stop joints, the lamp children
// SkywellGlow tints, the stowed envelope and the triangle budget. Called from VanguardBuilder.ValidateAndPreview.
using System;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // Runtime joint API (modules/Vanguard/Presentation/SkywellVisuals.cs). Renaming any of these breaks the kit.
        static readonly string[] SkywellJoints =
        {
            "Deck", "Winch", "Kite", "Tail", "Wing", "WingOuterL", "WingOuterR", "WingletL", "WingletR", "Nozzle", "Loader", "LoaderFore", "Cradle",
        };

        // Pit-stop additions: the runtime is null-safe without them, so a v4 model (none of them) only warns.
        static readonly string[] SkywellPitJoints = { "LatchL", "LatchR", "Round" };

        // FLIP TO true IN THE COMMIT THAT SHIPS THE v5 MODEL. From then on a model missing any pit joint or lamp child is rejected,
        // so a stale FBX cannot pass the batch silently (the runtime is a null-safe no-op for all of them). A model carrying SOME
        // of the new joints but not all is rejected regardless: that is a half-updated FBX, never a v4 one.
        const bool SkywellRequirePitJoints = false;

        // Joints whose lamps SkywellGlow tints: each needs a DIRECT child named Glow* (the LOD meshes hang under it).
        static readonly string[] SkywellGlowJoints = { "Deck", "Kite", "Cradle", "Nozzle", "WingletL", "WingletR" };

        // Stowed bounding box, sorted ascending (m): the measured 2.27 x 7.11 x 2.13 box plus a 0.02 m allowance per axis.
        static readonly float[] SkywellEnvelope = { 2.20f, 2.34f, 7.30f };

        const int SkywellMaxTriangles = 15000;

        static void ValidateSkywellKit(GameObject visual)
        {
            var joints = visual.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach (string name in SkywellJoints)
                if (!joints.ContainsKey(name)) throw new Exception("Skywell lost joint " + name);

            string[] noPitJoints = SkywellPitJoints.Where(n => !joints.ContainsKey(n)).ToArray();
            if (noPitJoints.Length > 0)
            {
                string message = "SKYWELL_PIT_JOINTS_MISSING " + string.Join(",", noPitJoints) + ": the cradle round and hold-down latches will not animate";
                if (SkywellRequirePitJoints || noPitJoints.Length < SkywellPitJoints.Length) throw new Exception(message);
                Debug.LogWarning("[Vanguard] " + message);
            }
            string[] noLamps = SkywellGlowJoints.Where(n => !joints[n].Cast<Transform>().Any(c => c.name.StartsWith("Glow", StringComparison.Ordinal))).ToArray();
            if (noLamps.Length > 0)
            {
                string message = "SKYWELL_GLOW_MISSING " + string.Join(",", noLamps) + ": those lamps will not change colour";
                if (SkywellRequirePitJoints || noLamps.Length < SkywellGlowJoints.Length) throw new Exception(message);
                Debug.LogWarning("[Vanguard] " + message);
            }

            Vector3 size = VisualBounds(visual).size;
            float[] sorted = { size.x, size.y, size.z };
            Array.Sort(sorted);
            for (int i = 0; i < sorted.Length; i++)
                if (sorted[i] > SkywellEnvelope[i])
                    throw new Exception("Skywell stowed box " + size + " exceeds the cargo-bay envelope " + string.Join(" x ", SkywellEnvelope));

            int triangles = visual.GetComponentsInChildren<MeshRenderer>(true).Where(r => LodLevel(r.name) == 0)
                .Sum(r => (int)r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
            if (triangles > SkywellMaxTriangles) throw new Exception("Skywell LOD0 has " + triangles + " triangles, budget " + SkywellMaxTriangles);
            Debug.Log("[Vanguard] SKYWELL_MODEL_OK joints=" + joints.Count + " pitJoints=" + (SkywellPitJoints.Length - noPitJoints.Length) +
                      "/" + SkywellPitJoints.Length + " box=" + size + " tris=" + triangles);
        }
    }
}
