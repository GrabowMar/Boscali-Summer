using System;
using BoscaliSummer.Features.Trenches.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Server-side harassing fire between opposing trench lines. One fire mission is a
    /// single mortar shell or a three-round artillery salvo of vanilla missiles,
    /// released above no-man's-land and aimed at the lines' midpoint with a radial
    /// miss. Vanilla owns flight, impact, damage, scorch and replication; this only
    /// schedules and bounds.
    /// </summary>
    internal sealed class TrenchBarrage
    {
        internal const string Prefix = "BoscaliSummer:TrenchBarrage:";
        private const float ReleaseAltitude = 4000f;
        private const float ReleaseSpeed = 900f;
        private const float InflightExpirySeconds = 45f;
        private const int TrackCeiling = 8;

        private readonly Missile[] inflight = new Missile[TrackCeiling];
        private readonly float[] expiry = new float[TrackCeiling];
        private int tracked;
        private int sequence;

        internal int Inflight
        {
            get
            {
                Prune(Time.time);
                return tracked;
            }
        }

        internal void Reset()
        {
            for (int i = 0; i < tracked; i++) inflight[i] = null;
            tracked = 0;
            sequence = 0;
        }

        /// <summary>
        /// Fires one mission from <paramref name="shooter"/> into the no-man's-land it
        /// shares with <paramref name="target"/>. Returns rounds released (a partial
        /// salvo when the sky is nearly full); zero when nothing left the tube.
        /// </summary>
        internal int Fire(TrenchLine shooter, TrenchLine target, float pairDistance, MissileDefinition shell, float now)
        {
            if (shooter == null || target == null || shell == null || shell.unitPrefab == null) return 0;
            if (shooter.OwnerHq == null) return 0;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return 0;
            Prune(now);
            int room = TrenchTraceMath.BarrageInflightCeiling - tracked;
            if (room <= 0) return 0;
            int rounds = Math.Min(room, TrenchTraceMath.BarrageRounds(pairDistance));
            float scatter = TrenchTraceMath.BarrageScatter(pairDistance);
            int fired = 0;
            for (int r = 0; r < rounds; r++)
            {
                TrenchTraceMath.BarrageAim(shooter.Center.x, shooter.Center.z, target.Center.x, target.Center.z,
                    UnityEngine.Random.value * 2f - 1f, UnityEngine.Random.value * 2f - 1f, scatter, out float x, out float z);
                Vector3 aim = TrenchTerrain.TryGround(x, z, out Vector3 ground) ? ground : new Vector3(x, 0f, z);
                Missile missile = spawner.SpawnSavedMissile(shell.unitPrefab,
                    (aim + Vector3.up * ReleaseAltitude).ToGlobalPosition(),
                    Quaternion.LookRotation(Vector3.down), shooter.OwnerHq, string.Empty, string.Empty,
                    Vector3.down * ReleaseSpeed, Prefix + shooter.Id + ":" + sequence++);
                if (missile == null) break;
                missile.SetAimpoint(aim.ToGlobalPosition(), Vector3.zero);
                missile.Arm();
                if (tracked < TrackCeiling)
                {
                    inflight[tracked] = missile;
                    expiry[tracked] = now + InflightExpirySeconds;
                    tracked++;
                }
                fired++;
            }
            return fired;
        }

        private void Prune(float now)
        {
            for (int i = tracked - 1; i >= 0; i--)
            {
                Missile missile = inflight[i];
                if (missile != null && !missile.disabled && now < expiry[i]) continue;
                tracked--;
                inflight[i] = inflight[tracked];
                expiry[i] = expiry[tracked];
                inflight[tracked] = null;
            }
        }
    }
}
