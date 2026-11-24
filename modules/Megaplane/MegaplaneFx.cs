using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Localized leaks, fire, soot and fragments from native aircraft effects. Each damage island
    /// keeps its own wound. Only the host advances the underlying fire damage.</summary>
    internal sealed class MegaplaneFx : MonoBehaviour
    {
        private const float BurnPerSecond = .7f, BurnSecondFire = 1.4f;
        private static readonly float[] Stages = { 75f, 50f, 25f };   // smoke, fire, second fire
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> Fire = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireEffect");
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> Leak = AccessTools.FieldRefAccess<FuelTank, GameObject>("leakEffect");
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> Ball = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireball");
        private static readonly AccessTools.FieldRef<DamageParticles, float> FireDamage = AccessTools.FieldRefAccess<DamageParticles, float>("fireDamage");
        private static readonly AccessTools.FieldRef<UnitPart, GameObject[]> Fragments = AccessTools.FieldRefAccess<UnitPart, GameObject[]>("disintegrationEffects");

        private Aircraft aircraft;
        private MegaplaneDamage damage;
        private GameObject fire, smoke, fireball, debris;
        private readonly Dictionary<AeroPart, int> stage = new Dictionary<AeroPart, int>();
        private readonly Dictionary<AeroPart, GameObject[]> trails = new Dictionary<AeroPart, GameObject[]>();
        private readonly Dictionary<AeroPart, Vector3> wound = new Dictionary<AeroPart, Vector3>();
        private readonly Dictionary<AeroPart, Vector3[]> skin = new Dictionary<AeroPart, Vector3[]>();   // island-local points on its art
        private float nextBurn;

        internal int Burning => stage.Count(s => s.Value >= 2);

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            damage = GetComponent<MegaplaneDamage>();
            foreach (FuelTank t in GetComponentsInChildren<FuelTank>(true))
            {
                fire = fire ?? Fire(t);
                smoke = smoke ?? Leak(t);
                fireball = fireball ?? Ball(t);
            }
            foreach (AeroPart p in GetComponentsInChildren<AeroPart>(true))
            {
                if (debris == null && Fragments(p) is GameObject[] f && f.Length > 0) debris = f[0];
                if (p.gameObject == gameObject) continue;
                AeroPart part = p;
                skin[part] = Sample(part);
                part.onApplyDamage += e => Hurt(part);
                part.onPartDetached += u => Broke(part);
            }
        }

        // Points on the island's own art (not its sub-islands'), so effects sit on the skin where the damage is.
        private static Vector3[] Sample(AeroPart part)
        {
            var points = new List<Vector3>();
            foreach (MeshFilter mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                var hitbox = mf.GetComponent<MegaplaneHitbox>();
                if (hitbox == null || hitbox.Owner != part || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                Vector3[] v = mf.sharedMesh.vertices;
                if (v.Length == 0 || mf.sharedMesh.bounds.size.magnitude < 3f) continue;
                for (int i = 0; i < 6; i++) points.Add(part.transform.InverseTransformPoint(mf.transform.TransformPoint(v[i * (v.Length - 1) / 5])));
            }
            return points.Count > 0 ? points.ToArray() : new[] { Vector3.zero };
        }

        private void Hurt(AeroPart part)
        {
            if (part == null || part.IsDetached()) return;
            int had = stage.TryGetValue(part, out int s) ? s : 0, now = 0;
            // A cold structure sheared off in one impact never passed through the combustion stages.
            if (part.hitPoints <= 0f && had < 2 && (MegaplaneDamage.Detachable(part) || MegaplaneDamage.Critical.Contains(part.name)))
            {
                return;
            }
            while (now < Stages.Length && part.hitPoints < Stages[now]) now++;
            if (now != had)
            {
                if (!trails.TryGetValue(part, out var trail)) trails[part] = trail = new GameObject[Stages.Length];
                Vector3[] sites = skin[part];
                if (!wound.TryGetValue(part, out Vector3 location)) wound[part] = location = sites[(part.id * 13 + 1) % sites.Length];
                Vector3 hit = part.transform.TransformPoint(location);
                for (int i = had; i < now; i++) trail[i] = Spawn(i == 0 ? smoke : fire, part, i == 0 ? .80f : i == 1 ? .80f : .55f, hit);
                for (int i = now; i < had; i++)
                    if (trail[i] != null) Destroy(trail[i]);
                if (now == 0) { stage.Remove(part); trails.Remove(part); wound.Remove(part); }
                else stage[part] = now;
                if (now > had && !damage.Restoring) Spawn(debris, part, .65f, hit);
            }
        }

        private void Broke(AeroPart part)
        {
            if (damage.Restoring) return;
            Vector3 root = part.transform.position;
            if (stage.TryGetValue(part, out int s) && s >= 2) Spawn(fireball, part, .90f, root);
            Spawn(debris, part, .85f, root);
            stage.Remove(part);
            trails.Remove(part);
            wound.Remove(part);
        }

        internal void PlantBreach(AeroPart support, Vector3 position)
        {
            if (support == null || position.y < Datum.LocalSeaY) return;
            Spawn(fireball, support, 1.4f, position);
            GameObject plume = Spawn(smoke, support, 1.65f, position);
            if (plume != null)
            {
                plume.name = "BS_Fx_ReactorVent";
                foreach (var ps in plume.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.startColor = new Color(.78f, .81f, .80f, .65f);
                }
                Destroy(plume, 7f);
            }
            var lamp = new GameObject("BS_Fx_ReactorFlash");
            lamp.transform.SetParent(support.transform);
            lamp.transform.position = position;
            var light = lamp.AddComponent<Light>();
            light.color = new Color(1f, .82f, .62f);
            light.range = 24f;
            light.intensity = 5f;
            Destroy(lamp, .20f);
        }

        private GameObject Spawn(GameObject prefab, AeroPart part, float size, Vector3? at = null)
        {
            if (prefab == null || part == null || part.transform.position.y < Datum.LocalSeaY) return null;
            Vector3[] points = skin.TryGetValue(part, out Vector3[] p) ? p : new[] { Vector3.zero };
            GameObject fx = Instantiate(prefab, part.transform);
            fx.name = "BS_Fx_" + prefab.name;   // MegaplaneDamage.ScaleEffects must not grow it a second time
            fx.transform.position = at ?? part.transform.TransformPoint(points[Random.Range(0, points.Length)]);
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (prefab == fire && ps.name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // In world space a fast ship draws a hundred-metre streak of flame. Anchor
                    // the flame to the wound and let the separate smoke system trail downwind.
                    main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                    main.customSimulationSpace = part.transform;
                    main.startSizeMultiplier *= .78f;
                }
            }
            foreach (Light lamp in fx.GetComponentsInChildren<Light>(true)) lamp.range = Mathf.Min(lamp.range, 9f);
            fx.transform.localScale = Vector3.one * size / Mathf.Max(part.transform.lossyScale.x, .01f);
            if (fx.TryGetComponent(out DamageParticles dp))
            {
                FireDamage(dp) = 0f;   // its overlap sphere would hit every hitbox of every island in reach: Update below burns instead
                part.AddHostedParticles(dp);
            }
            else Destroy(fx, 20f);
            return fx;
        }

        // Host: a fire left alone eats the island; at zero hit points MegaplaneDamage breaks it off (networked).
        private void Update()
        {
            if (Time.time < nextBurn || stage.Count == 0) return;
            nextBurn = Time.time + 1f;
            foreach (KeyValuePair<AeroPart, int> s in stage.ToList())
            {
                AeroPart part = s.Key;
                if (part == null || part.IsDetached()) continue;
                if (part.hitPoints >= Stages[s.Value - 1]) { Hurt(part); continue; }
                // the core (flight deck, fuselage) burns for show only: a fire must not kill the carrier on its own
                if (!aircraft.IsServer || s.Value < 2 || !MegaplaneDamage.Detachable(part) || MegaplaneDamage.Critical.Contains(part.name)) continue;
                part.TakeDamage(0f, 0f, 0f, 0f, s.Value >= 3 ? BurnSecondFire : BurnPerSecond, PersistentID.None);
                damage.AfterHit(part);
            }
        }
    }
}
