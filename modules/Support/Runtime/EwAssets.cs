using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>Wire-stable EW presence state. Sent to clients as a single byte; never the
    /// live Unit/Building reference itself.</summary>
    internal enum EwAssetState : byte
    {
        None = 0,
        Truck = 1,
        Encampment = 2
    }

    /// <summary>One faction's electronic-warfare presence: a mobile radar truck.
    /// At most one per faction. Wire byte 2 (Encampment) is reserved.</summary>
    internal sealed class EwAsset
    {
        public FactionHQ Owner;
        public EwAssetState State;
        public GroundVehicle Truck;
        public float Paid;

        public Vector3 Position => Truck != null ? Truck.transform.position : Vector3.zero;

        public bool Alive => Truck != null && !Truck.disabled;

        public float EffectMultiplier => 1.0f;
    }

    /// <summary>
    /// Host-authoritative registry of each faction's EW asset (at most one), mirroring the
    /// "spawn a persistent thing, track it, sweep for liveness" pattern
    /// <c>ZoneGarrisonManager</c> already uses for fortifications. Never crosses the wire
    /// except as a 1-byte state summary (<see cref="EwAssetState"/>) — a client cannot
    /// reconstruct the host's live Unit/Building identity from it, only its state.
    /// </summary>
    internal sealed class EwAssets
    {
        private const float LivenessInterval = 1f;

        private readonly Dictionary<FactionHQ, EwAsset> assets = new Dictionary<FactionHQ, EwAsset>();
        private float nextLivenessCheck;

        public EwAsset ForFaction(FactionHQ hq) =>
            hq != null && assets.TryGetValue(hq, out EwAsset asset) ? asset : null;

        public byte StateByteFor(FactionHQ hq)
        {
            EwAsset asset = ForFaction(hq);
            return asset == null ? (byte)0 : (byte)asset.State;
        }

        public bool TryDeployTruck(FactionHQ hq, GroundVehicle truck, float paid)
        {
            if (hq == null || truck == null || ForFaction(hq) != null) return false;
            assets[hq] = new EwAsset { Owner = hq, State = EwAssetState.Truck, Truck = truck, Paid = paid };
            return true;
        }

        /// <summary>Drop any asset whose spawned object was destroyed, mirroring
        /// ZoneGarrisonManager's per-second liveness sweep.</summary>
        public void Tick(float unscaledNow)
        {
            if (unscaledNow < nextLivenessCheck) return;
            nextLivenessCheck = unscaledNow + LivenessInterval;

            List<FactionHQ> dead = null;
            foreach (KeyValuePair<FactionHQ, EwAsset> entry in assets)
            {
                if (entry.Value.Alive) continue;
                (dead ??= new List<FactionHQ>(1)).Add(entry.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) assets.Remove(dead[i]);
        }

        public void Clear() => assets.Clear();
    }
}
