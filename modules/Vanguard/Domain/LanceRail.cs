using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// Server-side bank clock and rail heat of ONE station (all pods of a station charge, fire and heat together, so a
    /// twin-pod volley is a single release that costs one shot's heat and recharge). Pure state; LanceServer keys it by
    /// aircraft and station.
    /// </summary>
    internal sealed class LanceRail
    {
        public float Heat, HeatAt, ReadyAt;
        /// <summary>Station ammo at the previous release; a higher figure now means the pod was rearmed.</summary>
        public int LastAmmo = -1;

        /// <summary>
        /// A client's release of one volley at <paramref name="requested01"/> shot power. False = the bank cannot be ready yet.
        /// Otherwise the power is bounded by the time since the bank was ready, derated by the rails' heat, and the shot's
        /// heat and recharge are booked.
        /// </summary>
        public bool TryRelease(float requested01, float now, float airspeed, int ammo, out float power)
        {
            power = 0f;
            if (LastAmmo >= 0 && ammo > LastAmmo)
            {
                Heat = 0f; // rearmed: rails serviced, bank ready
                ReadyAt = 0f;
            }
            LastAmmo = ammo;
            if (now < ReadyAt - LanceCapacitor.ServerSlack) return false;
            float bound = LanceCapacitor.Power01(LanceCapacitor.ServerChargeBound(now - ReadyAt));
            Heat = LanceHeat.Step(Heat, airspeed, now - HeatAt);
            HeatAt = now;
            bool fault = LanceHeat.IsFault(Heat);
            power = Mathf.Min(requested01, bound) * (fault ? LanceHeat.FaultPower : LanceHeat.Derate(Heat));
            Heat += LanceHeat.ShotHeat(power);
            float recharge = LanceCapacitor.RechargeSeconds(power);
            ReadyAt = now + (fault ? Mathf.Max(recharge, LanceHeat.FaultLockout) : recharge);
            return true;
        }
    }
}
