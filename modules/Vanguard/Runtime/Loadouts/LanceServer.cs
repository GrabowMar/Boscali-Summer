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
    /// A station volley (one slug per pod) is ONE release that queues one pending entry per slug.
    /// </summary>
    internal static class LanceServer
    {
        private sealed class Shooter
        {
            public readonly LancePending Queue = new LancePending();
            public readonly Dictionary<byte, LanceRail> Rails = new Dictionary<byte, LanceRail>(2);

            public LanceRail Rail(byte station)
            {
                if (!Rails.TryGetValue(station, out LanceRail rail)) Rails[station] = rail = new LanceRail();
                return rail;
            }
        }

        private static readonly Dictionary<uint, Shooter> Shooters = new Dictionary<uint, Shooter>();
        private static readonly Dictionary<Unit, int> Crossed = new Dictionary<Unit, int>();
        private static readonly List<uint> Dead = new List<uint>();
        private static readonly RaycastHit[] Buffer = new RaycastHit[128];
        private static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
        private static float nextPrune;

        // Last chain, read by the sim.
        public static int LastUnits, LastParts, LastFriendly;
        public static float LastReach, LastSpan, LastFirstUnit, LastLastUnit;

        public static void Reset()
        {
            Shooters.Clear();
            Crossed.Clear();
            nextPrune = 0f;
            LastUnits = LastParts = LastFriendly = 0;
            LastReach = LastSpan = LastFirstUnit = LastLastUnit = 0f;
        }

        private static Shooter Of(Aircraft aircraft)
        {
            uint id = aircraft.persistentID.Id;
            if (!Shooters.TryGetValue(id, out Shooter shooter)) Shooters[id] = shooter = new Shooter();
            return shooter;
        }

        public static float HeatOf(Aircraft aircraft, byte station) =>
            Shooters.TryGetValue(aircraft.persistentID.Id, out Shooter s) && s.Rails.TryGetValue(station, out LanceRail rail) ? rail.Heat : 0f;

        /// <summary>Sim hook: pin the heat of every rail the server already tracks for this aircraft.</summary>
        public static void SetHeat(Aircraft aircraft, float heat)
        {
            if (!Shooters.TryGetValue(aircraft.persistentID.Id, out Shooter s)) return;
            foreach (LanceRail rail in s.Rails.Values)
            {
                rail.Heat = heat;
                rail.HeatAt = Time.time;
            }
        }

        /// <summary>Drops shooters whose aircraft is gone or dead (called a few times a second from LanceService).</summary>
        public static void Prune(float now)
        {
            if (now < nextPrune) return;
            nextPrune = now + 2f;
            Dead.Clear();
            foreach (uint id in Shooters.Keys)
                if (!UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) || unit == null || unit.disabled) Dead.Add(id);
            foreach (uint id in Dead) Shooters.Remove(id);
        }

        /// <summary>A shot the server already trusts (host or AI pod): remember its power for the coming hit claims, one per slug.</summary>
        public static void Enqueue(Aircraft aircraft, float power, float now, int count = 1)
        {
            LancePending queue = Of(aircraft).Queue;
            for (int i = 0; i < count; i++) queue.Add(power, now);
            VanguardStats.Bump("lance.pending", count);
        }

        /// <summary>
        /// A client's release of one station volley. Rejects a bank that cannot be ready yet, bounds the claimed power by
        /// the time since the bank was ready, derates it by the station's rail heat, then queues <paramref name="count"/>
        /// pending slugs at that power. False = rejected.
        /// </summary>
        public static bool Release(Aircraft aircraft, byte station, int ammo, float requested01, int count, float now, out float power, out float heat)
        {
            Shooter s = Of(aircraft);
            LanceRail rail = s.Rail(station);
            bool accepted = rail.TryRelease(requested01, now, aircraft.speed, ammo, out power);
            heat = rail.Heat;
            if (!accepted)
            {
                VanguardStats.Bump("lance.rejected");
                return false;
            }
            VanguardStats.Bump("lance.releases");
            Enqueue(aircraft, power, now, Math.Max(1, count));
            return true;
        }

        /// <summary>The ArmorPenetrate prefix body: pair the hit claim with the shooter's release, then run the chain.</summary>
        public static void Resolve(Vector3 position, Vector3 velocity, PersistentID dealer)
        {
            VanguardStats.Bump("lance.claims");
            UnitRegistry.TryGetUnit(dealer, out Unit shooter);
            float power = LanceCapacitor.UnmatchedPower;
            LanceClaim claim = LanceClaim.Unmatched;
            if (shooter != null && Shooters.TryGetValue(shooter.persistentID.Id, out Shooter s)) claim = s.Queue.Claim(Time.time, out power);
            if (claim == LanceClaim.Duplicate)
            {
                VanguardStats.Bump("lance.duplicates");
                return;
            }
            if (claim == LanceClaim.Unmatched) VanguardStats.Bump("lance.unmatched");
            try
            {
                Penetrate(position, velocity, power, shooter, dealer);
            }
            catch (Exception e)
            {
                // The prefix still swallows the claim: a throw here must not fall through into the vanilla full-pierce hit.
                VanguardStats.Bump("lance.errors");
                Plugin.Logger?.LogError("LANCE through-shot failed: " + e);
            }
        }

        private static int Cast(Vector3 origin, Vector3 dir, float distance) =>
            Physics.RaycastNonAlloc(origin, dir, Buffer, distance, ~(int)PhysicsLayers.ExclusionZonesMask, QueryTriggerInteraction.Ignore);

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
            // A claim's velocity is client-supplied (vanilla only clamps it to 1.5 x muzzle): the slug never beats its muzzle speed.
            float speed = Mathf.Min(velocity.magnitude, LanceCapacitor.MuzzleVelocity);
            if (speed < 1f) return;
            Vector3 dir = velocity.normalized;
            float reach = LanceSlug.ThroughReach(power);
            DamageEffects.BlastFrag(LanceCapacitor.Blast(power), position, dealer, PersistentID.None);

            // Start a hair behind the reported impact so the first collider is always inside the ray.
            Vector3 origin = position - dir * 0.3f;
            float castDistance = reach + 0.4f;
            int count = Cast(origin, dir, castDistance);
            // A full buffer holds an arbitrary subset, which can drop the near target: shorten the line until it all fits.
            for (int tries = 0; count >= Buffer.Length && tries < 4; tries++)
            {
                castDistance *= 0.5f;
                count = Cast(origin, dir, castDistance);
                VanguardStats.Bump("lance.castOverflow");
            }
            reach = Mathf.Min(reach, castDistance - 0.4f);
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
