using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // HAWC-X "SKIP" rig for modules/Vanguard/Presentation/HawcxVisuals.cs. BuildVanguard.finish() welds every non-joint
        // mesh into the glider, so the whole booster (tube, fins, collars, raceway, lugs, nozzle) must be one
        // g.seg("Booster") joint, and the sheath a g.seg("Sheath") joint whose Vanguard_Glow parts become its "Glow" children.
        static void HawcxRig(GameObject visual, Dictionary<string, Material> materials)
        {
            var nodes = visual.GetComponentsInChildren<Transform>(true);
            foreach (string joint in new[] { "Booster", "Sheath", "BodyFlapL", "BodyFlapR" })
                if (nodes.Count(t => t.name == joint) != 1)
                    throw new Exception("HawcX needs one '" + joint + "' joint: wrap its parts in g.seg(\"" + joint + "\", pivot=...) in models/HawcX.py");
            Transform booster = nodes.Single(t => t.name == "Booster"), sheath = nodes.Single(t => t.name == "Sheath");

            // Nothing of the booster may stay welded to the glider: no glider part reaches aft of the booster's middle.
            Vector3 fwd = visual.transform.parent.forward, span = new Vector3(Mathf.Abs(fwd.x), Mathf.Abs(fwd.y), Mathf.Abs(fwd.z));
            var lod0 = visual.GetComponentsInChildren<MeshRenderer>(true).Where(r => LodLevel(r.name) == 0).ToArray();
            var stage = lod0.Where(r => r.transform.IsChildOf(booster)).ToArray();
            Bounds b = stage[0].bounds;
            foreach (var r in stage.Skip(1)) b.Encapsulate(r.bounds);
            float middle = Vector3.Dot(b.center, fwd);
            var left = lod0.Where(r => !r.transform.IsChildOf(booster) &&
                Vector3.Dot(r.bounds.center, fwd) - Vector3.Dot(r.bounds.extents, span) < middle).Select(r => r.name).ToArray();
            if (left.Length > 0) throw new Exception("HawcX booster parts left on the glider (" + string.Join(", ", left) + "): move them into g.seg(\"Booster\")");
            if (!stage.Any(r => r.sharedMaterial == materials["Dark"])) throw new Exception("HawcX Booster joint has no nozzle (Vanguard_Dark) parts");

            // A joint is always a skin carrier; the sheath is its Glow children (three LODs), hidden until the glow phase.
            var glow = sheath.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterial == materials["Glow"]).ToArray();
            if (glow.Length < 3) throw new Exception("HawcX Sheath joint needs Vanguard_Glow parts (Glow + _LOD1/_LOD2), found " + glow.Length);
            foreach (var r in glow) r.enabled = false;
            Debug.Log("[Vanguard] HAWCX_RIG_OK booster=" + stage.Length + " sheathGlow=" + glow.Length);
        }
    }
}
