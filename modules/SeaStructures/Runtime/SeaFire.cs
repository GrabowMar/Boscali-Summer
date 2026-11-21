using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// The platform's fire set, borrowed from the donor warship's own FuelTank prefabs (the Megaplane pattern):
    /// its leak smoke, flame and fireball are already authored and pooled by the game, so a hit section burns
    /// like a native ship. The fire particles' own damage is zeroed: the damage model, not a stray overlap
    /// sphere, decides what a burning point costs.
    /// </summary>
    internal sealed class SeaFire
    {
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> FireFx = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireEffect");
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> LeakFx = AccessTools.FieldRefAccess<FuelTank, GameObject>("leakEffect");
        private static readonly AccessTools.FieldRef<FuelTank, GameObject> BallFx = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireball");
        private static readonly AccessTools.FieldRef<DamageParticles, float> FireDamage = AccessTools.FieldRefAccess<DamageParticles, float>("fireDamage");

        private readonly GameObject fire, smoke, ball;

        private SeaFire(GameObject fire, GameObject smoke, GameObject ball)
        {
            this.fire = fire;
            this.smoke = smoke;
            this.ball = ball;
        }

        internal static SeaFire Bind(GameObject structure)
        {
            GameObject fire = null, smoke = null, ball = null;
            foreach (FuelTank tank in structure.GetComponentsInChildren<FuelTank>(true))
            {
                fire = fire != null ? fire : FireFx(tank);
                smoke = smoke != null ? smoke : LeakFx(tank);
                ball = ball != null ? ball : BallFx(tank);
            }
            return new SeaFire(fire, smoke, ball);
        }

        internal bool HasFire => fire != null || smoke != null;

        /// <summary>Hang a flame (or smoke when <paramref name="flame"/> is false) on a damage point.</summary>
        internal GameObject Attach(Transform parent, Vector3 local, float scale, bool flame, UnitPart part = null)
        {
            GameObject prefab = flame ? fire : smoke;
            if (prefab == null || parent == null) return null;
            GameObject fx = Object.Instantiate(prefab, parent, false);
            fx.name = flame ? "BS_SeaFlame" : "BS_SeaSmoke";
            fx.transform.localPosition = local;
            fx.transform.localRotation = Quaternion.identity;
            fx.transform.localScale = Vector3.one * scale;
            foreach (ParticleSystem system in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            if (fx.TryGetComponent(out DamageParticles damage)) FireDamage(damage) = 0f;
            damage = fx.GetComponentInChildren<DamageParticles>(true);
            if (damage != null)
            {
                FireDamage(damage) = 0f;
                part?.AddHostedParticles(damage);
            }
            return fx;
        }

        /// <summary>One authored fireball at a world point. Warship donors carry no <see cref="FuelTank"/>
        /// (that component belongs to aircraft), so the bundled fx-fuel-fireball serves every blast; the
        /// native branch stays for a donor that ever does bring a tank. Either way the borrowed rule
        /// holds: whatever particles this spawns deal no damage of their own.</summary>
        internal GameObject Ball(Vector3 world, float scale)
        {
            if (ball != null && Datum.origin != null)
            {
                GameObject fx = Object.Instantiate(ball, Datum.origin, false);
                fx.name = "BS_SeaFireball";
                fx.transform.SetPositionAndRotation(world, Quaternion.identity);
                fx.transform.localScale = Vector3.one * scale;
                foreach (DamageParticles damage in fx.GetComponentsInChildren<DamageParticles>(true))
                    FireDamage(damage) = 0f;
                Object.Destroy(fx, 12f);
                return fx;
            }
            GameObject spawned = ParticleFx.Spawn("fx-fuel-fireball", world, Quaternion.identity, scale);
            if (spawned != null)
                foreach (DamageParticles damage in spawned.GetComponentsInChildren<DamageParticles>(true))
                    FireDamage(damage) = 0f;
            return spawned;
        }
    }
}
