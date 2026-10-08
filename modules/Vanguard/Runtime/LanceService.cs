using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Private Gun handles for the LANCE charge cycle. Missing members fall back to vanilla fire.</summary>
    internal static class LanceAccess
    {
        public static bool Available { get; private set; }
        private static bool tried;
        private static AccessTools.FieldRef<Gun, float> muzzleVelocity;
        private static AccessTools.FieldRef<Gun, float> tracerSize;
        private static AccessTools.FieldRef<Gun, int> bulletsLoaded;
        private delegate void SpawnBulletFn(Gun gun, float timeOffset);
        private static SpawnBulletFn spawnBullet;

        public static bool Ensure()
        {
            if (Available) return true;
            if (tried) return false;
            tried = true;
            try
            {
                muzzleVelocity = AccessTools.FieldRefAccess<Gun, float>("muzzleVelocity");
                tracerSize = AccessTools.FieldRefAccess<Gun, float>("tracerSize");
                bulletsLoaded = AccessTools.FieldRefAccess<Gun, int>("bulletsLoaded");
                spawnBullet = AccessTools.MethodDelegate<SpawnBulletFn>(AccessTools.Method(typeof(Gun), "SpawnBullet"));
                Available = muzzleVelocity != null && tracerSize != null && bulletsLoaded != null && spawnBullet != null;
            }
            catch (Exception e)
            {
                Available = false;
                Plugin.Logger?.LogWarning("LANCE charge access unavailable: " + e.Message);
            }
            if (!Available) Plugin.Logger?.LogWarning("LANCE falls back to vanilla gun behaviour.");
            return Available;
        }

        public static int BulletsLoaded(Gun gun) => bulletsLoaded(gun);
        public static void SetBulletsLoaded(Gun gun, int value) => bulletsLoaded(gun) = value;
        public static void SetMuzzleVelocity(Gun gun, float value) => muzzleVelocity(gun) = value;
        public static void SetTracerSize(Gun gun, float value) => tracerSize(gun) = value;
        public static void Fire(Gun gun) => spawnBullet(gun, 0f);
    }

    /// <summary>
    /// RG-12 LANCE capacitor control. Gun.Fire is suppressed for the pod (LanceFirePatch); each call only
    /// records trigger-held, and when the calls stop the service releases one slug scaled by hold time.
    /// Single-mag pod: when the 12 rounds are gone the trigger goes dead, as vanilla.
    /// </summary>
    internal sealed class LanceService : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.lance";
        private const string Channel = "lance";

        private sealed class Charge
        {
            public Gun Gun;
            public WeaponStation Station;
            public float Start = -99f;
            public float LastHold = -99f;
            public float ReadyAt;
            public float RechargeTotal;
            public WeaponInfo Clone;
        }

        private static readonly Dictionary<Gun, Charge> Charges = new Dictionary<Gun, Charge>();

        private readonly List<Gun> gone = new List<Gun>();
        private IHudBoard board;
        private IHudLine line;

        public static bool IsLance(Gun gun) =>
            gun != null && gun.info != null && gun.info.name.Contains(VanguardKeys.LanceInfo);

        public void ResetForScene()
        {
            Charges.Clear();
            gone.Clear();
            line?.Release();
            line = null;
        }

        /// <summary>One trigger-held tick from the Fire patch. Never fires; Update fires on release.</summary>
        public static void Hold(Gun gun, WeaponStation station)
        {
            if (gun == null || station == null || gun.Safety || !LanceAccess.Ensure()) return;
            if (!Charges.TryGetValue(gun, out Charge c))
            {
                c = new Charge { Gun = gun };
                Charges[gun] = c;
            }
            if (gun.info != c.Clone) // first hold, or Rearm swapped the shared asset back in
            {
                c.Clone = UnityEngine.Object.Instantiate(gun.info);
                gun.info = c.Clone;
            }
            c.Station = station;
            gun.SetWeaponStation(station);
            float now = Time.time;
            if (LanceAccess.BulletsLoaded(gun) <= 0 || station.Ammo <= 0 || now < c.ReadyAt)
            {
                c.LastHold = -99f; // dry or recharging: drop any banked hold
                return;
            }
            if (c.LastHold < 0f || now - c.LastHold > LanceCapacitor.ReleaseGap) c.Start = now;
            c.LastHold = now;
        }

        private void Update()
        {
            float now = Time.time;
            gone.Clear();
            foreach (KeyValuePair<Gun, Charge> pair in Charges)
            {
                Charge c = pair.Value;
                if (c.Gun == null)
                {
                    gone.Add(pair.Key);
                    continue;
                }
                if (c.LastHold > 0f && now - c.LastHold > LanceCapacitor.ReleaseGap && now >= c.ReadyAt) Fire(c, now);
            }
            for (int i = 0; i < gone.Count; i++) Charges.Remove(gone[i]);
            Hud(now);
        }

        private static void Fire(Charge c, float now)
        {
            c.LastHold = -99f;
            Gun gun = c.Gun;
            int loaded = LanceAccess.BulletsLoaded(gun);
            if (c.Station == null || c.Station.Ammo <= 0 || loaded <= 0)
            {
                c.RechargeTotal = 0.3f; // dry click: brief lockout, no recharge
                c.ReadyAt = now + c.RechargeTotal;
                return;
            }
            float power = LanceCapacitor.Power01(LanceCapacitor.Charge(now - c.Start));
            c.Clone.blastDamage = LanceCapacitor.Blast(power);
            c.Clone.pierceDamage = LanceCapacitor.Pierce(power);
            c.Clone.muzzleVelocity = LanceCapacitor.Velocity(power);
            LanceAccess.SetMuzzleVelocity(gun, LanceCapacitor.Velocity(power));
            LanceAccess.SetTracerSize(gun, LanceCapacitor.Tracer(power));
            LanceAccess.Fire(gun);
            LanceAccess.SetBulletsLoaded(gun, loaded - 1);
            gun.ammo--;
            c.Station.UpdateLastFired(1);
            c.Station.Updated();
            c.RechargeTotal = LanceCapacitor.RechargeSeconds(power);
            c.ReadyAt = now + c.RechargeTotal;
        }

        private void Hud(float now)
        {
            if (board == null && !ModuleServices.TryGet(out board)) return;
            board.DeclareChannel(Channel, "LANCE");
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled)
            {
                line?.Release();
                line = null;
                return;
            }
            Charge mine = null;
            foreach (Charge c in Charges.Values)
                if (c.Gun != null && c.Gun.attachedUnit == me)
                {
                    mine = c;
                    break;
                }
            if (mine == null)
            {
                line?.Release();
                line = null;
                return;
            }
            if (mine.LastHold > 0f && now - mine.LastHold <= LanceCapacitor.ReleaseGap)
            {
                float charge = LanceCapacitor.Charge(now - mine.Start);
                line ??= board.Acquire(Owner, Channel, "charge");
                if (charge >= 1f) line?.Set(HudTone.Info, "LANCE READY", "RELEASE TO FIRE", 1f);
                else line?.Set(HudTone.Caution, "LANCE CHARGE", "HOLD TO POWER UP", charge);
            }
            else if (now < mine.ReadyAt && mine.RechargeTotal > 0f)
            {
                line ??= board.Acquire(Owner, Channel, "charge");
                line?.Set(HudTone.Warning, "LANCE RECHARGE", "CAPACITOR BANK", 1f - (mine.ReadyAt - now) / mine.RechargeTotal);
            }
            else
            {
                line?.Release();
                line = null;
            }
        }
    }
}
