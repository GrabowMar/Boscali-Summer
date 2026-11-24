using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Smoke is shed into the moving air from two surviving wounds, then outlives the falling airframe.</summary>
    internal sealed class MegaplaneFlightSmoke : MonoBehaviour
    {
        private sealed class Vent
        {
            internal MeshFilter Skin;
            internal AeroPart Owner;
            internal bool Engine;
            internal Vector3 Last;
            internal float LastTime;
            internal bool HasSample;
        }

        private Aircraft aircraft;
        private MegaplaneFx damage;
        private MegaplaneCrash crash;
        private ParticleSystem cloud;
        private Vent[] vents;
        private Vent hull, engine;
        private System.Random random;
        private float inspectAt;

        private void Start()
        {
            if (GameManager.IsHeadless) { enabled = false; return; }
            aircraft = GetComponent<Aircraft>();
            damage = GetComponent<MegaplaneFx>();
            crash = GetComponent<MegaplaneCrash>();
        }

        private void FindVents()
        {
            var found = new List<Vent>();
            foreach (MeshFilter mesh in GetComponentsInChildren<MeshFilter>(true))
            {
                string key = mesh.name;
                bool pod = key.Length == 6 && key.StartsWith("Pod_", StringComparison.Ordinal);
                if (!pod && key != "Fuselage_Aft" && key != "Fuselage_Center" && key != "Fuselage_Forward" &&
                    key != "Wing_Root_L" && key != "Wing_Root_R" && key != "Wing_Inner_L" && key != "Wing_Inner_R") continue;
                var owner = mesh.GetComponentInParent<AeroPart>();
                if (owner != null && mesh.sharedMesh != null)
                    found.Add(new Vent { Skin = mesh, Owner = owner, Engine = pod });
            }
            vents = found.ToArray();
            random = new System.Random(unchecked((int)(aircraft.NetId * 3931u)));
        }

        private void Inspect()
        {
            Vent nextHull = null, nextEngine = null;
            if (aircraft.disabled || damage.Burning > 0)
            {
                if (vents == null) FindVents();
                float bestHull = -1f, bestEngine = -1f;
                foreach (Vent vent in vents)
                {
                    if (vent.Owner == null || vent.Skin == null || vent.Owner.IsDetached()) continue;
                    float loss = Mathf.Clamp01(1f - vent.Owner.hitPoints * .01f);
                    if (!aircraft.disabled && loss < .50f) continue;
                    // The first smoke comes from the wounded section. A disabled aircraft
                    // with missing fires vents from a remaining powerplant and upper hull.
                    float score = loss + (vent.Skin.name == "Fuselage_Center" || vent.Skin.name == "Pod_L1" ? .04f : 0f);
                    if (vent.Engine && score > bestEngine) { nextEngine = vent; bestEngine = score; }
                    if (!vent.Engine && score > bestHull) { nextHull = vent; bestHull = score; }
                }
            }
            if (nextHull != null && nextHull != hull) nextHull.HasSample = false;
            if (nextEngine != null && nextEngine != engine) nextEngine.HasSample = false;
            hull = nextHull;
            engine = nextEngine;
        }

        private void LateUpdate()
        {
            if (aircraft == null || Datum.origin == null || crash.Impacted) { enabled = false; return; }
            if (Time.time >= inspectAt) { inspectAt = Time.time + .35f; Inspect(); }
            if (hull == null && engine == null) return;
            if (cloud == null) cloud = MegaplaneSmokeCloud.Create(Datum.origin, "Groza smoke trail", 320, aircraft.NetId);
            if (cloud == null) return;
            Vector3 wind = MegaplaneSmokeCloud.Wind;
            Trail(hull, wind);
            Trail(engine, wind);
        }

        private void Trail(Vent source, Vector3 wind)
        {
            if (source == null || source.Owner == null || source.Owner.IsDetached() || source.Skin == null) return;
            var mesh = source.Skin;
            Bounds bounds = mesh.sharedMesh.bounds;
            Vector3 local = bounds.center + new Vector3(0f, bounds.extents.y * .7f, -bounds.extents.z * .20f);
            Vector3 at = mesh.transform.TransformPoint(local);
            if (at.y <= Datum.LocalSeaY) { source.HasSample = false; return; }
            Vector3 global = at - Datum.originPosition;
            Vector3 previous = source.HasSample ? source.Last : global;
            float distance = Vector3.Distance(global, previous);
            bool disabled = aircraft.disabled;
            float spacing = disabled ? 8f : 12f;
            if (distance < spacing && source.HasSample && Time.time - source.LastTime < (disabled ? .13f : .23f)) return;
            // This also drops a stale trail point after a teleport or long suspended frame.
            if (distance > 170f) { previous = global; distance = 0f; }
            int count = Mathf.Clamp(Mathf.CeilToInt(distance / spacing), 1, 6);
            float density = disabled ? 1f : Mathf.InverseLerp(55f, 0f, source.Owner.hitPoints) * .67f + .18f;
            float width = Mathf.Lerp(4.5f, source.Engine ? 10.5f : 14f, density);
            for (int i = 1; i <= count; i++)
            {
                Vector3 point = Datum.originPosition + Vector3.Lerp(previous, global, i / (float)count);
                float spin = (float)random.NextDouble();
                Vector3 eddy = new Vector3(Mathf.Sin(spin * 40f), 0f, Mathf.Cos(spin * 38f));
                Vector3 drift = wind + Vector3.up * (2.4f + 3f * spin) + eddy * 2.5f;
                float shade = Mathf.Lerp(.42f, .20f + .12f * spin, density);
                MegaplaneSmokeCloud.Emit(cloud, point + eddy * 1.3f, drift, width * (.84f + spin * .32f),
                    9f + (3f + 4f * spin) * density, new Color(shade * 1.04f, shade, shade * .94f, .33f + .49f * density),
                    -170f + spin * 340f);
            }
            source.Last = global;
            source.LastTime = Time.time;
            source.HasSample = true;
        }

        private void OnDisable()
        {
            if (cloud != null)
            {
                cloud.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                Destroy(cloud.gameObject, 17f);
                cloud = null;
            }
        }
    }
}
