// Carriage fit: the box a donor store fills on its rack against the box ours fills, each in the station's own frame.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // Box of every visible mesh under `root` that `keep` accepts, in `frame`'s local space (a rolled rack station inflates a world AABB).
        static Bounds? LocalBounds(Transform frame, GameObject root, Func<MeshFilter, bool> keep = null)
        {
            Bounds? box = null;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!filter.sharedMesh || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
                if (keep != null && !keep(filter)) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));
                    Vector3 point = frame.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    Bounds grown = box ?? new Bounds(point, Vector3.zero);
                    grown.Encapsulate(point);
                    box = grown;
                }
            }
            return box;
        }

        static string Box(Bounds? b) => b == null ? "none" :
            "x " + b.Value.min.x.ToString("F2") + ".." + b.Value.max.x.ToString("F2") +
            " y " + b.Value.min.y.ToString("F2") + ".." + b.Value.max.y.ToString("F2") +
            " z " + b.Value.min.z.ToString("F2") + ".." + b.Value.max.z.ToString("F2");

        const float Rail = 0.13f;     // underside of the game's launch rail above a railed station (AGM_heavy and ARM1 racks, from FitReport)
        const float Strip = 0.08f;    // half width of the strip of a store that meets the rail
        const float FitSlack = 1.05f; // a store may overhang its slot by 5 % before it is shrunk

        // Racks dropped from the spec table. The project folder keeps their assets and everything in it ships.
        static readonly string[] RetiredMounts =
        {
            "VG_HawcX_AGM_heavy_single", "VG_HawcX_CruiseMissile1_internalx2", "VG_HawcX_CruiseMissile1_internal",
            "VG_Remora_AGM_heavy_single", "VG_Remora_CruiseMissile1_internalx2",
        };

        static void RetireMounts()
        {
            foreach (string json in RetiredMounts)
                foreach (string asset in new[] { "WM_" + json + ".asset", json + ".prefab", "Op_" + json + ".asset" })
                    AssetDatabase.DeleteAsset(R + asset);
        }

        static bool Detail(MeshFilter filter) => LodLevel(filter.name) == 0;

        // Highest LOD0 vertex of the visual within Strip of the slot's centreline: the surface that meets the rail, not a
        // fin or wingtip beside it. Its bounding top when no vertex lies in the strip.
        static float TopUnderRail(Transform frame, GameObject visual, Bounds slot, Bounds store)
        {
            float top = float.NegativeInfinity;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!Detail(filter) || !filter.GetComponent<MeshRenderer>().enabled) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 p = frame.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (Mathf.Abs(p.x - store.center.x) <= Strip && Mathf.Abs(p.z - store.center.z) <= slot.extents.z) top = Mathf.Max(top, p.y);
                }
            }
            return float.IsNegativeInfinity(top) ? store.max.y : top;
        }

        // Hangs a carried visual in the donor store's slot, in its parent station's frame: top on the rail (where the donor's
        // top was on a rack without one), centred sideways and fore-aft, and shrunk (never grown) when it would reach into
        // the next station or, in a bay, past the donor's length. Carriage only: the launched weapon keeps its full size.
        static void Seat(GameObject visual, Bounds? slot, bool railed, float pitch, bool bay, string label)
        {
            Transform frame = visual.transform.parent;
            Bounds? measured = LocalBounds(frame, visual, Detail);
            if (slot == null || measured == null) throw new Exception(label + ": nothing to measure for the carriage fit");
            Bounds s = slot.Value, o = measured.Value;
            float k = Mathf.Min(1f, FitSlack * Mathf.Min(pitch / o.size.x, bay ? s.size.z / o.size.z : float.MaxValue));
            visual.transform.localScale *= k;
            o = LocalBounds(frame, visual, Detail).Value;
            float top = TopUnderRail(frame, visual, s, o);
            visual.transform.localPosition += new Vector3(s.center.x - o.center.x, (railed ? Rail : s.max.y) - top, s.center.z - o.center.z);
            Debug.Log("[Vanguard] FIT " + label + " scale=" + k.ToString("F2") + " store=" + o.size.ToString("F2") + " slot=" + s.size.ToString("F2") +
                " top=" + top.ToString("F2") + " boxTop=" + o.max.y.ToString("F2") + (railed ? " railed" : "") + (bay ? " bay" : ""));
        }

        // A pod hangs straight off the hardpoint, root y = 0, where the game's own ECM and flare pods end: the lift that puts its top there.
        static Vector3 PodLift(GameObject root, GameObject pod) =>
            Vector3.down * LocalBounds(root.transform, pod, Detail).Value.max.y;

        // Unity -executeMethod Vanguard.VanguardBuilder.FitReport: logs every rack's slot and store boxes, changes nothing.
        public static void FitReport()
        {
            foreach (string path in Directory.GetFiles(D + "GameObject", "*_PLACEHOLDER.prefab").Concat(Directory.GetFiles(R, "VG_*.prefab")))
            {
                string name = Path.GetFileNameWithoutExtension(path).Replace("_PLACEHOLDER", "");
                bool ours = name.StartsWith("VG_", StringComparison.Ordinal);
                // Ours, and the donor racks they are built from (the pods' three carrier types included).
                if (!ours && !Missiles.SelectMany(s => s.Racks).Concat(new[] { "AGM_heavy_single", "ECMPod1", "SpecialFlarePod" }).Contains(name)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                if (!prefab) continue;
                var g = UnityEngine.Object.Instantiate(prefab);
                var stations = g.GetComponentsInChildren<MountedMissile>(true);
                Debug.Log("[Vanguard] FITREPORT " + name + " stations=" + stations.Length + " all: " + Box(LocalBounds(g.transform, g)) +
                    " hardware: " + Box(LocalBounds(g.transform, g, f => !f.GetComponentInParent<MountedMissile>(true))));
                foreach (var station in stations)
                {
                    Vector3 at = g.transform.InverseTransformPoint(station.transform.position);
                    Vector3 up = g.transform.InverseTransformDirection(station.transform.up);
                    Debug.Log("[Vanguard] FITREPORT   " + name + "/" + station.name + " at " + at.ToString("F2") + " up " + up.ToString("F2") +
                        " store: " + Box(LocalBounds(station.transform, station.gameObject)) +
                        (ours ? " lod0: " + Box(LocalBounds(station.transform, station.gameObject, f => LodLevel(f.name) == 0)) : ""));
                }
                UnityEngine.Object.DestroyImmediate(g);
            }
        }
    }
}
