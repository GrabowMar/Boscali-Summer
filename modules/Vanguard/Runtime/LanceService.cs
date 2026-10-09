using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Presentation;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Private Gun handles for the LANCE charge cycle. Missing members fall back to vanilla fire.</summary>
    internal static class LanceAccess
    {
        public static bool Available { get; private set; }
        private static bool tried;
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
                tracerSize = AccessTools.FieldRefAccess<Gun, float>("tracerSize");
                bulletsLoaded = AccessTools.FieldRefAccess<Gun, int>("bulletsLoaded");
                spawnBullet = AccessTools.MethodDelegate<SpawnBulletFn>(AccessTools.Method(typeof(Gun), "SpawnBullet"));
                Available = tracerSize != null && bulletsLoaded != null && spawnBullet != null;
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
        public static void SetTracerSize(Gun gun, float value) => tracerSize(gun) = value;
        public static void Fire(Gun gun) => spawnBullet(gun, 0f);
    }

    internal enum LancePhase : byte { Idle, Charging, Cycling, Recharging, Fault }

    /// <summary>What a pod looks like right now; the same record drives HUD, strips, barrel and audio on every peer.</summary>
    internal struct LanceLook
    {
        public float Charge, Heat, SinceShot, LastPower, Recharge01;
        public bool Charging, Fault;
    }

    /// <summary>One LANCE gun. The owner peer runs the state machine; other peers mirror it from LanceFx messages.</summary>
    internal sealed class LancePod
    {
        public readonly Gun Gun;
        public readonly Aircraft Aircraft;
        public WeaponStation Station;
        public LancePodView View;
        public LancePhase Phase;
        public float HoldStart, LastHold, Heat, CycleEnd, ReadyAt, RechargeTotal, LastPower, NextSend;
        public float ShotAt = -99f;
        public int LastAmmo = -1;
        // Observer mirror (LanceFx): last charge level and the power of the last server-confirmed shot.
        public float RemoteCharge, RemoteAt = -99f, FxPower, FxAt = -99f;

        public LancePod(Gun gun, Aircraft aircraft)
        {
            Gun = gun;
            Aircraft = aircraft;
        }

        public bool Owner => Aircraft != null && Aircraft.LocalSim;

        /// <summary>0 idle, 1 charging, 2 recharging, 3 fault (sim status).</summary>
        public int StateCode => Phase == LancePhase.Charging ? 1 : Phase == LancePhase.Fault ? 3 : Phase == LancePhase.Idle ? 0 : 2;

        public int Rank => Phase == LancePhase.Charging ? 3 : Phase != LancePhase.Idle ? 2 : Heat > 0.12f ? 1 : 0;

        /// <summary>0..1 engine draw: the bank while it charges, then a decaying tail while it primes.</summary>
        public float Draw(float now)
        {
            LanceLook look = Look(now);
            return look.Charging ? LanceDraw.ChargeDraw(look.Charge)
                : look.Recharge01 >= 0f ? LanceDraw.RechargeDraw(LastPower, look.Recharge01) : 0f;
        }

        public WeaponStation ResolveStation()
        {
            if (Station == null && Aircraft != null)
                Station = Aircraft.weaponStations.Find(s => s.Weapons.Contains(Gun));
            return Station;
        }

        public LanceLook Look(float now)
        {
            var look = new LanceLook { Heat = Heat, SinceShot = ShotAt > 0f ? now - ShotAt : -1f, LastPower = LastPower, Recharge01 = -1f };
            if (Owner)
            {
                if (Phase == LancePhase.Charging)
                {
                    look.Charging = true;
                    look.Charge = LanceCapacitor.Charge(now - HoldStart);
                }
                else if (Phase != LancePhase.Idle && RechargeTotal > 0f)
                    look.Recharge01 = Mathf.Clamp01(1f - (ReadyAt - now) / RechargeTotal);
                look.Fault = Phase == LancePhase.Fault;
                return look;
            }
            float age = now - RemoteAt;
            if (age < 0.6f && RemoteCharge > 0f)
            {
                look.Charging = true;
                look.Charge = Mathf.Min(1f, RemoteCharge + 0.4f * Mathf.Min(age, 0.5f));
            }
            else if (look.SinceShot >= 0f)
            {
                float refill = look.SinceShot / (LanceCapacitor.CyclingSeconds + LanceCapacitor.RechargeSeconds(LastPower));
                look.Recharge01 = refill < 1f ? refill : -1f;
            }
            return look;
        }
    }

    /// <summary>
    /// RG-12 LANCE OVERCHARGE, owner peer. Gun.Fire is suppressed for the pod (LanceFirePatch); each call only records
    /// trigger-held, and when the calls stop the service releases one slug scaled by hold time. The bank drains the
    /// engines while it charges (LanceEngineDraw), the rails heat with every shot, a full charge punches through
    /// everything on the line (LanceServer). The server decides every damage number; clients send the held charge.
    /// </summary>
    internal sealed class LanceService : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.lance";
        private const string Channel = "lance";

        public static readonly Dictionary<Gun, LancePod> Pods = new Dictionary<Gun, LancePod>();
        /// <summary>AI pods release at 85 % charge; the sim switches this off to hold full charges.</summary>
        public static bool AutoRelease = true;
        private static LanceService instance;

        private readonly List<Gun> gone = new List<Gun>();
        private IHudBoard board;
        private IHudLine chargeLine, heatLine, busLine;

        private void Awake() => instance = this;

        public static bool IsLance(Gun gun) =>
            gun != null && (Pods.ContainsKey(gun) || (gun.info != null && gun.info.name.Contains(VanguardKeys.LanceInfo)));

        public void ResetForScene()
        {
            Pods.Clear();
            gone.Clear();
            ReleaseLines();
            LanceServer.Reset();
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (instance == this) instance = null;
        }

        public static LancePod Register(Gun gun, Aircraft aircraft)
        {
            if (Pods.TryGetValue(gun, out LancePod pod)) return pod;
            pod = new LancePod(gun, aircraft);
            Pods[gun] = pod;
            if (!GameManager.IsHeadless) pod.View = LancePodView.Attach(pod);
            return pod;
        }

        public static LancePod PodFor(Aircraft aircraft, byte stationNumber)
        {
            if (aircraft == null || stationNumber >= aircraft.weaponStations.Count) return null;
            foreach (Weapon weapon in aircraft.weaponStations[stationNumber].Weapons)
                if (weapon is Gun gun && Pods.TryGetValue(gun, out LancePod pod)) return pod;
            return null;
        }

        public static void Notice(HudTone tone, string text, string detail = null)
        {
            if (instance != null && instance.board != null) instance.board.Notice(Channel, tone, text, detail);
        }

        /// <summary>One trigger-held tick from the Fire patch. Never fires; Update fires on release.</summary>
        public static void Hold(Gun gun, WeaponStation station)
        {
            if (gun == null || station == null || !LanceAccess.Ensure()) return;
            if (!Pods.TryGetValue(gun, out LancePod pod)) pod = Register(gun, gun.attachedUnit as Aircraft);
            Aircraft aircraft = pod.Aircraft;
            if (aircraft == null || !aircraft.LocalSim || gun.Safety || !gun.IsAttached()) return;
            pod.Station = station;
            gun.SetWeaponStation(station);
            // Linked-guns fire reaches every gun station; only the selected Lance station may charge.
            if (aircraft.weaponManager.currentWeaponStation != station) return;
            float now = Time.time;
            if (pod.Phase == LancePhase.Charging)
            {
                pod.LastHold = now;
                return;
            }
            if (pod.Phase != LancePhase.Idle || LanceAccess.BulletsLoaded(gun) <= 0 || station.Ammo <= 0) return;
            pod.Phase = LancePhase.Charging;
            pod.HoldStart = pod.LastHold = pod.NextSend = now;
            VanguardStats.Bump("lance.charges");
        }

        private void Update()
        {
            float now = Time.time, dt = Time.deltaTime;
            gone.Clear();
            GameManager.GetLocalAircraft(out Aircraft me);
            LancePod mine = null;
            foreach (KeyValuePair<Gun, LancePod> pair in Pods)
            {
                LancePod pod = pair.Value;
                if (pod.Gun == null || pod.Aircraft == null)
                {
                    gone.Add(pair.Key);
                    continue;
                }
                if (pod.Aircraft.disabled) continue;
                pod.Heat = LanceHeat.Step(pod.Heat, pod.Aircraft.speed, dt);
                if (pod.Owner) Tick(pod, now);
                if (pod.Aircraft == me && (mine == null || pod.Rank > mine.Rank)) mine = pod;
            }
            for (int i = 0; i < gone.Count; i++) Pods.Remove(gone[i]);
            Hud(mine, now);
        }

        private static void Tick(LancePod pod, float now)
        {
            Aircraft aircraft = pod.Aircraft;
            WeaponStation station = pod.ResolveStation();
            if (station == null) return;
            if (pod.LastAmmo >= 0 && station.Ammo > pod.LastAmmo) // rearmed: rails serviced, bank ready
            {
                pod.Heat = 0f;
                if (pod.Phase != LancePhase.Charging) pod.Phase = LancePhase.Idle;
            }
            pod.LastAmmo = station.Ammo;
            switch (pod.Phase)
            {
                case LancePhase.Charging:
                    if (!CanHold(pod))
                    {
                        pod.Phase = LancePhase.Idle; // switched station, gear down, pod gone, rounds gone: no shot, no ammo
                        LanceNet.SendCancel(pod);
                        VanguardStats.Bump("lance.cancels");
                        break;
                    }
                    float charge = LanceCapacitor.Charge(now - pod.HoldStart);
                    bool held = now - pod.LastHold <= LanceCapacitor.ReleaseGap;
                    if (!held || (aircraft.Player == null && AutoRelease && charge >= LanceCapacitor.AiReleaseCharge)) Release(pod, now);
                    else if (now >= pod.NextSend)
                    {
                        pod.NextSend = now + 0.25f;
                        LanceNet.SendCharge(pod, charge);
                    }
                    break;
                case LancePhase.Cycling:
                    if (now >= pod.CycleEnd) pod.Phase = LancePhase.Recharging;
                    break;
                case LancePhase.Recharging:
                case LancePhase.Fault:
                    if (now >= pod.ReadyAt)
                    {
                        pod.Phase = LancePhase.Idle;
                        if (aircraft.Player != null) Notice(HudTone.Info, "LANCE READY");
                    }
                    break;
            }
        }

        private static bool CanHold(LancePod pod) =>
            pod.Gun != null && pod.Station != null && pod.Aircraft.weaponManager.currentWeaponStation == pod.Station &&
            !pod.Station.SafetyIsOn(pod.Aircraft) && pod.Gun.IsAttached() && !pod.Gun.Safety &&
            pod.Station.Ammo > 0 && LanceAccess.BulletsLoaded(pod.Gun) > 0;

        private static void Release(LancePod pod, float now)
        {
            Gun gun = pod.Gun;
            WeaponStation station = pod.Station;
            Aircraft aircraft = pod.Aircraft;
            float requested = LanceCapacitor.Power01(LanceCapacitor.Charge(pod.LastHold - pod.HoldStart));
            bool fault = LanceHeat.IsFault(pod.Heat);
            float power = requested * (fault ? LanceHeat.FaultPower : LanceHeat.Derate(pod.Heat));
            // The server learns the shot BEFORE the vanilla bullet exists, so its hit claim always finds this release.
            if (GameAccess.IsServer())
            {
                LanceServer.Enqueue(aircraft, power, now);
                LanceNet.ServerShot(pod, power);
            }
            else LanceNet.SendRelease(pod, requested);
            LanceAccess.SetTracerSize(gun, LanceCapacitor.Tracer(power));
            LanceAccess.Fire(gun);
            LanceAccess.SetBulletsLoaded(gun, LanceAccess.BulletsLoaded(gun) - 1);
            gun.ammo--;
            station.UpdateLastFired(1);
            station.Updated();

            pod.Heat += LanceHeat.ShotHeat(power);
            float recharge = LanceCapacitor.CyclingSeconds + (fault
                ? Mathf.Max(LanceCapacitor.RechargeSeconds(power), LanceHeat.FaultLockout)
                : LanceCapacitor.RechargeSeconds(power));
            pod.Phase = fault ? LancePhase.Fault : LancePhase.Cycling;
            pod.CycleEnd = now + LanceCapacitor.CyclingSeconds;
            pod.ReadyAt = now + recharge;
            pod.RechargeTotal = recharge;
            pod.LastPower = power;
            pod.ShotAt = now;
            VanguardStats.Bump("lance.shots");
            if (fault) VanguardStats.Bump("lance.faults");

            Kick(aircraft, gun, power);
            if (pod.View != null) pod.View.PlayShot(power); // Unity null check: the view may have been destroyed
            if (aircraft.Player != null)
            {
                if (fault) Notice(HudTone.Warning, "RAIL FAULT - COOLING");
                if (GameManager.GetLocalAircraft(out Aircraft me) && me == aircraft)
                {
                    LanceCapacitor.Shake(power, out float low, out float high);
                    SceneSingleton<CameraStateManager>.i?.ShakeCamera(low, high);
                }
            }
        }

        // VelocityChange keeps the kick mass-independent; positive spin about +right pitches the nose DOWN, so nose-up is -right.
        private static void Kick(Aircraft aircraft, Gun gun, float power)
        {
            Rigidbody rb = aircraft.rb;
            if (rb == null) return;
            LanceCapacitor.Kick(power, out float back, out float noseUp);
            rb.AddForce(-gun.transform.forward * back, ForceMode.VelocityChange);
            rb.AddTorque(-aircraft.transform.right * noseUp, ForceMode.VelocityChange);
        }

        // ---- HUD (local player's aircraft only)

        private void Hud(LancePod pod, float now)
        {
            if (board == null && !ModuleServices.TryGet(out board)) return;
            board.DeclareChannel(Channel, "LANCE");
            if (pod == null || !pod.Owner || pod.Aircraft.Player == null || pod.Aircraft.disabled)
            {
                ReleaseLines();
                return;
            }
            LanceLook look = pod.Look(now);
            // charge / recharge / fault line
            if (look.Charging)
            {
                chargeLine ??= board.Acquire(Owner, Channel, "charge");
                if (look.Charge >= 0.995f) chargeLine?.Set(HudTone.Info, "LANCE READY", "RELEASE TO FIRE", 1f);
                else chargeLine?.Set(HudTone.Caution, "LANCE CHARGE", TierWord(LanceCapacitor.TierOf(look.Charge)), look.Charge);
            }
            else if (pod.Phase == LancePhase.Fault)
            {
                chargeLine ??= board.Acquire(Owner, Channel, "charge");
                chargeLine?.Set(HudTone.Warning, "RAIL FAULT", "COOLING  " + Mathf.Max(0f, pod.ReadyAt - now).ToString("0.0"), Mathf.Max(0f, look.Recharge01));
            }
            else if (pod.Phase != LancePhase.Idle)
            {
                chargeLine ??= board.Acquire(Owner, Channel, "charge");
                chargeLine?.Set(HudTone.Warning, "LANCE RECHARGE", "BANK PRIMING", Mathf.Max(0f, look.Recharge01));
            }
            else
            {
                chargeLine?.Release();
                chargeLine = null;
            }
            // rail heat line
            if (pod.Heat >= 0.12f || look.Charging)
            {
                heatLine ??= board.Acquire(Owner, Channel, "heat");
                HudTone tone = pod.Heat >= LanceHeat.DerateStart ? HudTone.Warning : pod.Heat >= LanceHeat.HotAt ? HudTone.Caution : HudTone.Info;
                heatLine?.Set(tone, "RAIL HEAT", HeatWord(pod.Heat), Mathf.Min(pod.Heat, 1f));
            }
            else
            {
                heatLine?.Release();
                heatLine = null;
            }
            // bus draw line
            float sag = LanceEngineDraw.SagOf(pod.Aircraft);
            float draw = pod.Draw(now);
            if (draw >= 0.05f)
            {
                busLine ??= board.Acquire(Owner, Channel, "bus");
                busLine?.Set(HudTone.Caution, "BUS DRAW", sag > 0f ? "AB OFFLINE  -" + Mathf.RoundToInt(sag * 100f) + "% THRUST" : "AB OFFLINE", draw);
            }
            else
            {
                busLine?.Release();
                busLine = null;
            }
            // cockpit: the stutter is the vanilla jam effect on the HUD markers plus a low hum
            if (look.Charging)
            {
                float level = LanceDraw.FlickerLevel(look.Charge);
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null && level > 0f) hud.jamAccumulation = Mathf.Max(hud.jamAccumulation, 0.35f * level);
                if (level > 0.5f) SceneSingleton<CameraStateManager>.i?.ShakeCamera(0.03f * level, 0.06f * level);
            }
        }

        private static string TierWord(LanceTier tier) =>
            tier == LanceTier.Snap ? "SNAP" : tier == LanceTier.Punch ? "PUNCH" : "THROUGH-SHOT";

        private static string HeatWord(float heat)
        {
            switch (LanceHeat.BandOf(heat))
            {
                case HeatBand.Cool: return "NOMINAL";
                case HeatBand.Warm: return "WARM";
                case HeatBand.Hot: return "HOT";
                case HeatBand.Derated: return "DERATED -" + Mathf.RoundToInt((1f - LanceHeat.Derate(heat)) * 100f) + "% POWER";
                default: return "OVERHEAT  NEXT SHOT FAULTS";
            }
        }

        private void ReleaseLines()
        {
            chargeLine?.Release();
            heatLine?.Release();
            busLine?.Release();
            chargeLine = heatLine = busLine = null;
        }
    }
}
