using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Damage you can see on the G-240. Vanilla aircraft only burn through their FuelTank parts, which on the carrier
    /// sit at the donor bomber's spots inside the hull and never reach their ignition damage, and the donor islands carry no
    /// damage effects: so the carrier never smoked or burned. This borrows the tank's own leak, fire and fireball prefabs
    /// (scaled to a 380 m hull) and puts them on the island's skin as it takes damage: under 75 hp it trails smoke, under 50
    /// it burns, under 25 a second fire; every heavy hit throws debris. A burning island keeps losing hit points (host), so an
    /// unchecked fire ends with the piece breaking off. On a break: a fireball at the root, debris, and the falling piece
    /// burns all the way down. Every peer draws its own (damage and detach are replicated).</summary>
    internal sealed class MegaplaneFx : MonoBehaviour
    {
        private const float Scale = 4.5f, BurnPerSecond = .7f, BurnSecondFire = 1.4f;
        private static readonly float[] Stages = { 75f, 50f, 25f };   // smoke, fire, second fire
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> Fire = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireEffect");
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> Leak = AccessTools.FieldRefAccess<FuelTank, GameObject>("leakEffect");
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> Ball = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireball");
        private static readonly AccessTools.FieldRef<DamageParticles, float> FireDamage = AccessTools.FieldRefAccess<DamageParticles, float>("fireDamage");
        private static readonly AccessTools.FieldRef<UnitPart, GameObject[]> Fragments = AccessTools.FieldRefAccess<UnitPart, GameObject[]>("disintegrationEffects");

        private Aircraft aircraft;
        private GameObject fire, smoke, fireball, debris;
        private readonly Dictionary<AeroPart, int> stage = new Dictionary<AeroPart, int>();
        private readonly Dictionary<AeroPart, Vector3[]> skin = new Dictionary<AeroPart, Vector3[]>();   // island-local points on its art
        private float nextBurn;

        internal int Burning => stage.Count(s => s.Value >= 2);

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
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
                if (mf.name.StartsWith("GrozaLOD_") || mf.GetComponentInParent<AeroPart>() != part || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                Vector3[] v = mf.sharedMesh.vertices;
                if (v.Length == 0 || mf.sharedMesh.bounds.size.magnitude < 3f) continue;
                for (int i = 0; i < 6; i++) points.Add(part.transform.InverseTransformPoint(mf.transform.TransformPoint(v[Random.Range(0, v.Length)])));
            }
            return points.Count > 0 ? points.ToArray() : new[] { Vector3.zero };
        }

        private void Hurt(AeroPart part)
        {
            if (part == null || part.IsDetached()) return;
            int had = stage.TryGetValue(part, out int s) ? s : 0, now = 0;
            while (now < Stages.Length && part.hitPoints < Stages[now]) now++;
            for (int i = had; i < now; i++) Spawn(i == 0 ? smoke : fire, part, i == 0 ? 1f : 1.3f);
            if (now > had) { stage[part] = now; Spawn(debris, part, 1.5f); }
            Scorch(part);
        }

        // Vanilla skins darken through the _HitPoints shader; the carrier's own materials are tinted toward soot instead.
        private static readonly Color Soot = new Color(.16f, .15f, .14f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private readonly Dictionary<AeroPart, Renderer[]> skins = new Dictionary<AeroPart, Renderer[]>();
        private MaterialPropertyBlock block;

        private void Scorch(AeroPart part)
        {
            if (!skins.TryGetValue(part, out Renderer[] rs))
                skins[part] = rs = part.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer && r.GetComponentInParent<AeroPart>() == part && !r.name.StartsWith("BS_Fx")).ToArray();
            float k = Mathf.Clamp01(1f - part.hitPoints * .01f) * .85f;
            block = block ?? new MaterialPropertyBlock();
            foreach (Renderer r in rs)
            {
                if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(BaseColor)) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColor, Color.Lerp(r.sharedMaterial.GetColor(BaseColor), Soot, k));
                r.SetPropertyBlock(block);
            }
        }

        private void Broke(AeroPart part)
        {
            Vector3 root = part.transform.position;
            Spawn(fireball, part, 2f, root);
            Spawn(debris, part, 2.5f, root);
            if (!stage.TryGetValue(part, out int s) || s < 2) Spawn(fire, part, 1.6f);   // the falling piece burns all the way down
            stage.Remove(part);
        }

        private void Spawn(GameObject prefab, AeroPart part, float size, Vector3? at = null)
        {
            if (prefab == null || part == null || part.transform.position.y < Datum.LocalSeaY) return;
            Vector3[] points = skin.TryGetValue(part, out Vector3[] p) ? p : new[] { Vector3.zero };
            GameObject fx = Instantiate(prefab, part.transform);
            fx.name = "BS_Fx_" + prefab.name;   // MegaplaneDamage.ScaleEffects must not grow it a second time
            fx.transform.position = at ?? part.transform.TransformPoint(points[Random.Range(0, points.Length)]);
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            fx.transform.localScale = Vector3.one * Scale * size / Mathf.Max(part.transform.lossyScale.x, .01f);
            if (fx.TryGetComponent(out DamageParticles dp))
            {
                FireDamage(dp) = 0f;   // its overlap sphere would hit every hitbox of every island in reach: Update below burns instead
                part.AddHostedParticles(dp);
            }
            else Destroy(fx, 20f);
        }

        // Host: a fire left alone eats the island; at zero hit points MegaplaneDamage breaks it off (networked).
        private void Update()
        {
            if (!aircraft.IsServer || Time.time < nextBurn || stage.Count == 0) return;
            nextBurn = Time.time + 1f;
            MegaplaneDamage damage = GetComponent<MegaplaneDamage>();
            foreach (KeyValuePair<AeroPart, int> s in stage.ToList())
            {
                AeroPart part = s.Key;
                // the core (flight deck, fuselage) burns for show only: a fire must not kill the carrier on its own
                if (s.Value < 2 || part == null || part.IsDetached() || !MegaplaneDamage.Detachable(part) || MegaplaneDamage.Critical.Contains(part.name)) continue;
                part.TakeDamage(0f, 0f, 0f, 0f, s.Value >= 3 ? BurnSecondFire : BurnPerSecond, PersistentID.None);
                if (damage != null) damage.AfterHit(part, part.hitPoints);
            }
        }
    }
}
