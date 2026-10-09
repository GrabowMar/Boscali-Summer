using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// LANCE server authority. Every number that hurts something is decided here: the effective shot power (the client's
    /// claim bounded by the server's bank clock and rail heat), the through-chain reach, which units it crosses and what
    /// each takes. The only client-supplied geometry is the vanilla hit claim (Unit.CmdClaimHit, plausibility-checked by
    /// the game), which keeps what-you-see-is-what-you-hit. LanceArmorPenetratePatch routes every Lance impact here.
    /// </summary>
    internal static class LanceServer
    {
        private sealed class Pending
        {
            public float Power, At, UsedAt;
            public bool Used;
        }

        private sealed class Shooter
        {
            public float Heat, HeatAt, ReadyAt;
            public readonly List<Pending> Queue = new List<Pending>(4);
        }

        private const int MaxPending = 4;
        private const float DuplicateWindow = 0.5f; // a ricochet of the same slug claims again within ~0.1 s

        private static readonly Dictionary<uint, Shooter> Shooters = new Dictionary<uint, Shooter>();
        private static readonly Dictionary<Unit, int> Crossed = new Dictionary<Unit, int>();
        private static readonly RaycastHit[] Buffer = new RaycastHit[64];
        private static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        // Last chain, read by the sim.
        public static int LastUnits, LastParts, LastFriendly;
        public static float LastReach, LastSpan, LastFirstUnit, LastLastUnit;

        public static void Reset()
        {
            Shooters.Clear();
            Crossed.Clear();
            LastUnits = LastParts = LastFriendly = 0;
            LastReach = LastSpan = LastFirstUnit = LastLastUnit = 0f;
        }

        private static Shooter Of(Aircraft aircraft)
        {
            uint id = aircraft.persistentID.Id;
            if (!Shooters.TryGetValue(id, out Shooter shooter)) Shooters[id] = shooter = new Shooter();
            return shooter;
        }

        public static float HeatOf(Aircraft aircraft) => Shooters.TryGetValue(aircraft.persistentID.Id, out Shooter s) ? s.Heat : 0f;

        public static void SetHeat(Aircraft aircraft, float heat)
        {
            Shooter s = Of(aircraft);
            s.Heat = heat;
            s.HeatAt = Time.time;
        }

        public static void Clear(Aircraft aircraft) => Shooters.Remove(aircraft.persistentID.Id);

        /// <summary>A shot the server already trusts (host or AI pod): remember its power for the coming hit claim.</summary>
        public static void Enqueue(Aircraft aircraft, float power, float now)
        {
            List<Pending> queue = Of(aircraft).Queue;
            queue.Add(new Pending { Power = power, At = now });
            if (queue.Count > MaxPending) queue.RemoveAt(0);
            VanguardStats.Bump("lance.pending");
        }

        /// <summary>
        /// A client's release. Rejects a bank that cannot be ready yet, bounds the claimed power by the time since the
        /// bank was ready, derates it by the server's rail heat, then enqueues it. False = rejected.
        /// </summary>
        public static bool Release(Aircraft aircraft, float requested01, float now, out float power, out float heat)
        {
            Shooter s = Of(aircraft);
            power = 0f;
            heat = s.Heat;
            if (now < s.ReadyAt - LanceCapacitor.ServerSlack)
            {
                VanguardStats.Bump("lance.rejected");
                return false;
            }
            float bound = LanceCapacitor.Power01(LanceCapacitor.ServerChargeBound(now - s.ReadyAt));
            s.Heat = LanceHeat.Step(s.Heat, aircraft.speed, now - s.HeatAt);
            s.HeatAt = now;
            bool fault = LanceHeat.IsFault(s.Heat);
            power = Mathf.Min(requested01, bound) * (fault ? LanceHeat.FaultPower : LanceHeat.Derate(s.Heat));
            s.Heat += LanceHeat.ShotHeat(power);
            heat = s.Heat;
            float recharge = LanceCapacitor.RechargeSeconds(power);
            s.ReadyAt = now + (fault ? Mathf.Max(recharge, LanceHeat.FaultLockout) : recharge);
            VanguardStats.Bump("lance.releases");
            Enqueue(aircraft, power, now);
            return true;
        }

        /// <summary>The ArmorPenetrate prefix body: pair the hit claim with the shooter's release, then run the chain.</summary>
        public static void Resolve(Vector3 position, Vector3 velocity, PersistentID dealer)
        {
            float now = Time.time;
            VanguardStats.Bump("lance.claims");
            UnitRegistry.TryGetUnit(dealer, out Unit shooter);
            float power = LanceCapacitor.UnmatchedPower;
            bool matched = false, duplicate = false;
            if (shooter != null && Shooters.TryGetValue(shooter.persistentID.Id, out Shooter s))
                for (int i = s.Queue.Count - 1; i >= 0 && !matched; i--)
                {
                    Pending p = s.Queue[i];
                    if (now - p.At > LanceCapacitor.PendingTtl) break;
                    if (!p.Used)
                    {
                        p.Used = true;
                        p.UsedAt = now;
                        power = p.Power;
                        matched = true;
                    }
                    else if (now - p.UsedAt < DuplicateWindow) duplicate = true;
                }
            if (!matched && duplicate)
            {
                VanguardStats.Bump("lance.duplicates");
                return;
            }
            if (!matched) VanguardStats.Bump("lance.unmatched");
            Penetrate(position, velocity, power, shooter, dealer);
        }

        /// <summary>
        /// One slug from <paramref name="position"/> along <paramref name="velocity"/>. Crosses up to 6 units / 20 parts
        /// within the power's reach, each hit a quarter weaker per unit and slower by what the armour ate. Friendly units
        /// beyond the first impact are skipped (no damage, no slowdown); terrain and sea end the line.
        /// </summary>
        public static void Penetrate(Vector3 position, Vector3 velocity, float power, Unit shooter, PersistentID dealer)
        {
            if (!GameAccess.IsServer())
            {
                VanguardStats.Bump("lance.clientDamage");
                return;
            }
            float speed = velocity.magnitude;
            if (speed < 1f) return;
            Vector3 dir = velocity / speed;
            float reach = LanceSlug.ThroughReach(power);
            DamageEffects.BlastFrag(LanceCapacitor.Blast(power), position, dealer, PersistentID.None);

            // Start a hair behind the reported impact so the first collider is always inside the ray.
            Vector3 origin = position - dir * 0.3f;
            int count = Physics.RaycastNonAlloc(origin, dir, Buffer, reach + 0.4f, ~(int)PhysicsLayers.ExclusionZonesMask, QueryTriggerInteraction.Ignore);
            Array.Sort(Buffer, 0, count, ByDistance);
            Crossed.Clear();
            int parts = 0, friendly = 0;
            Vector3 end = position;
            float firstUnit = 0f, lastUnit = 0f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Buffer[i];
                PhysicMaterial material = hit.collider.sharedMaterial;
                if (material == GameAssets.i.terrainMaterial || material == GameAssets.i.WaterMaterial || hit.point.y < Datum.LocalSeaY)
                {
                    end = hit.point;
                    break;
                }
                IDamageable damageable = hit.collider.gameObject.GetComponent<IDamageable>();
                if (damageable == null) continue; // props and scenery: the slug passes through
                Unit unit = damageable.GetUnit();
                if (unit != null && unit == shooter) continue;
                int index;
                bool entered = false;
                if (unit == null) index = Math.Max(0, Crossed.Count - 1);
                else if (!Crossed.TryGetValue(unit, out index))
                {
                    if (Crossed.Count > 0 && shooter != null && unit.NetworkHQ != null && unit.NetworkHQ == shooter.NetworkHQ)
                    {
                        friendly++;
                        continue;
                    }
                    if (Crossed.Count >= LanceSlug.MaxUnits) break;
                    index = Crossed.Count;
                    Crossed[unit] = index;
                    entered = true;
                }
                if (++parts > LanceSlug.MaxParts) break;
                float damage = LanceSlug.HitPierce(power, speed, Vector3.Dot(dir, -hit.normal), index);
                damageable.TakeDamage(damage, 1f, 0f, 0f, 0f, dealer);
                if (parts == 1) firstUnit = damage;
                if (entered) lastUnit = damage; // first part of the most recent unit: the sim compares it with the first unit's
                end = hit.point;
                speed = LanceSlug.Residual(speed, damage, damageable.GetArmorProperties().pierceArmor);
                if (!LanceSlug.Continues(speed)) break;
            }
            LastUnits = Crossed.Count;
            LastParts = parts;
            LastFriendly = friendly;
            LastReach = reach;
            LastSpan = Vector3.Distance(position, end);
            LastFirstUnit = firstUnit;
            LastLastUnit = lastUnit;
            VanguardStats.Bump("lance.chains");
            VanguardStats.Bump("lance.chainUnits", Crossed.Count);
            VanguardStats.Bump("lance.chainParts", parts);
            if (friendly > 0) VanguardStats.Bump("lance.friendlySkips", friendly);
            if (Crossed.Count >= 2 || LanceSlug.ThroughEnabled(power)) LanceNet.ServerBeam(dealer, position, end, Crossed.Count);
        }
    }
}
