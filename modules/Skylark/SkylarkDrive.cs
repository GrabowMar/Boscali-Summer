using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    internal enum DriveMode : byte { Auto, Ev, Hybrid }

    /// <summary>
    /// The hybrid drive. The fans are electric; a turbogenerator feeds them and a traction battery can carry them alone.
    /// AUTO picks its own mode: at low power with charge in hand the turbine shuts down (EV: no fuel burn, a fifth of the
    /// heat, under half the noise, 92 % thrust, and heat seekers find nothing to lock), otherwise the turbine runs and recharges the pack.
    /// EV holds the stealth mode until the pack is nearly empty or a hover at full throttle sinks (EV LIMIT), HYBRID keeps the turbine on.
    /// With the tanks dry the drive is EV whatever was chosen: the battery flies the aircraft home. At full throttle below 100 m/s with
    /// the turbine on, BOOST adds the pack to the turbine for 15 % more thrust (heavy lift, hard climbs) until the charge runs low.
    /// The mode follows the replicated engine state and the owner's choice (SkylarkNet), so the host's seekers and each client's sound agree.
    /// </summary>
    internal sealed class SkylarkDrive : MonoBehaviour
    {
        private const float EnterBelow = .55f, LeaveAbove = .62f, EnterCharge = .25f;
        /// <summary>The pack's reserve: EV falls back to HYBRID here, and the Halberd's coils stop firing below it.</summary>
        internal const float Floor = .05f;
        /// <summary>Charge one Halberd round takes from the pack.</summary>
        internal const float ShotCharge = .001f;
        private const float Heat = .2f, FanNoise = .45f, TurbineNoise = .15f, EvThrust = .92f;
        private const float BoostGain = .15f, BoostIn = .35f, BoostOut = .1f, BoostDrain = .02f, BoostMaxSpeed = 100f;
        private const float LimitSink = -1f, LimitSeconds = 2f, HeavyRatio = 1.05f;
        // Intake doors and exhaust shutters swing about the lateral axis when the turbine is off; signs to be set by eye against the art.
        // Vertical hinges, authored open (art contract: Blender +37.5/+72 about Z on the right side; Unity flips the sign). Index 0 = left.
        private const float IntakeClosed = 37.5f, ShutterClosed = 72f, DoorSeconds = 1f;
        private static readonly HashSet<Unit> Silent = new HashSet<Unit>();
        private static readonly string[] Percent = new string[101], Words = { "AUTO EV", "AUTO HYB", "EV", "HYBRID", "CHARGING", "BOOST", "DRY EV", "HEAVY" };
        private static readonly string[] Texts = new string[Words.Length * 2];
        private static readonly float[] Ticks = { EnterCharge, Floor };
        private static Aircraft gaugeAircraft;
        private static SkylarkDrive gaugeDrive;

        [SerializeField] private Transform[] intakeDoors, exhaustShutters;
        [SerializeField] private Renderer[] exhaustGlows;
        private MaterialPropertyBlock exhaustBlock;
        private float shownGlow = -1f;
        private SkylarkHinge[] hinges;
        private Aircraft aircraft;
        private ControlInputs inputs;
        private Turbofan engine;
        private JetNozzle[] nozzles;
        private AudioSource turbine;
        private SkylarkMast mast;
        private float[] irMin, irMax, fanVolume;
        private float fuelMin, fuelMax, turbineVolume, thrust, loss, sinking, nextSlow, born;
        private int warned, gunAmmo = -1;
        private float nextGun;
        private bool heavyTold, dryTold;

        /// <summary>Traction battery, 0..1. A full pack gives about three minutes of hover on the fans alone.</summary>
        internal float Charge { get; private set; } = 1f;
        internal bool Electric { get; private set; }
        internal bool Boost { get; private set; }
        internal float FanThrustScale => Electric ? EvThrust : Boost ? 1f + BoostGain : 1f;
        /// <summary>The tanks are empty: the battery alone drives the fans.</summary>
        internal bool Dry { get; private set; }
        /// <summary>Thrust to weight on the turbine alone is under 1.05: a vertical lift needs BOOST (owner peer, sampled each second).</summary>
        internal bool Heavy { get; private set; }
        internal float ThrustToWeight { get; private set; } = 2f;
        internal DriveMode Mode { get; private set; }
        /// <summary>Host: Halberd rounds charged to the pack and the charge they took (sim readback).</summary>
        internal int GunShots { get; private set; }
        internal float GunDrain { get; private set; }

        internal static bool IsSilent(Unit unit) => Silent.Contains(unit);
        /// <summary>Every live Skylark drive on this peer (the host resends their state to late joiners).</summary>
        internal static readonly List<SkylarkDrive> All = new List<SkylarkDrive>();

        internal void Configure(Dictionary<string, Transform> nodes)
        {
            Transform[] Pair(string name) => new[] { nodes.TryGetValue(name + "_L", out Transform l) ? l : null, nodes.TryGetValue(name + "_R", out Transform r) ? r : null };
            intakeDoors = Pair("Intake_Door");
            exhaustShutters = Pair("Exhaust_Shutter");
            exhaustGlows = new[]
            {
                nodes.TryGetValue("ExhaustGlow_L", out Transform gl) ? gl.GetComponent<Renderer>() : null,
                nodes.TryGetValue("ExhaustGlow_R", out Transform gr) ? gr.GetComponent<Renderer>() : null
            };
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            born = Time.time;
            inputs = aircraft.GetInputs();
            mast = GetComponent<SkylarkMast>();
            All.Add(this);
            engine = GetComponentInChildren<Turbofan>();
            nozzles = SkylarkTemplate.Get<JetNozzle[]>(GetComponent<DuctedThrustSystem>(), "nozzles");
            turbine = SkylarkTemplate.Get<AudioSource>(engine, "turbineAudio");
            turbineVolume = turbine != null ? turbine.volume : 0f;
            fuelMin = SkylarkTemplate.Get<float>(engine, "fuelConsumptionMin");
            fuelMax = SkylarkTemplate.Get<float>(engine, "fuelConsumptionMax");
            loss = SkylarkTemplate.Get<float>(engine, "parasiticThrustLoss");
            thrust = SkylarkTemplate.Get<float>(engine, "staticThrust");
            irMin = new float[nozzles.Length]; irMax = new float[nozzles.Length]; fanVolume = new float[nozzles.Length];
            for (int i = 0; i < nozzles.Length; i++)
            {
                irMin[i] = SkylarkTemplate.Get<float>(nozzles[i], "IRMin");
                irMax[i] = SkylarkTemplate.Get<float>(nozzles[i], "IRMax");
                fanVolume[i] = SkylarkTemplate.Get<float>(nozzles[i], "thrustMaxVolume");
            }
            var list = new List<SkylarkHinge>();
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                if (intakeDoors[i] != null) list.Add(new SkylarkHinge(transform, intakeDoors[i], Vector3.up, side * IntakeClosed));
                if (exhaustShutters[i] != null) list.Add(new SkylarkHinge(transform, exhaustShutters[i], Vector3.up, side * ShutterClosed));
            }
            hinges = list.ToArray();
        }

        /// <summary>Any peer: the drive mode its pilot chose (the host sends it on, see SkylarkNet).</summary>
        internal void SetMode(DriveMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            if (GameManager.IsLocalAircraft(aircraft)) SkylarkControl.Say("DRIVE: " + (mode == DriveMode.Auto ? "AUTO" : mode == DriveMode.Ev ? "EV, SILENT" : "HYBRID"));
        }

        private void Update()
        {
            bool local = GameManager.IsLocalAircraft(aircraft);
            if (Time.time >= nextSlow) Sample(local);
            float demand = Mathf.Clamp01(engine.GetThrustRatio());
            bool running = aircraft.Ignition && !aircraft.disabled;
            if (Mode == DriveMode.Ev && running && !Dry && GameAccess.IsServer())
            {
                // EV LIMIT: full throttle near the hover and still sinking means the battery cannot lift this weight.
                bool limit = Electric && inputs.throttle > .95f && aircraft.speed < 30f && aircraft.rb.velocity.y < LimitSink;
                sinking = limit ? sinking + Time.deltaTime : 0f;
                // Host decides and tells everyone (clients wait for the state, so no peer flips back and forth).
                if (Charge <= Floor || sinking > LimitSeconds)
                {
                    Mode = DriveMode.Hybrid;
                    if (local) SkylarkControl.Say(Charge <= Floor ? "BATTERY LOW, HYBRID" : "EV LIMIT: TOO HEAVY, HYBRID");
                    sinking = 0f;
                    SkylarkNet.Broadcast(aircraft);
                }
            }
            bool electric = running && (Dry ? Charge > 0f
                : Mode == DriveMode.Ev || Mode == DriveMode.Auto && (Electric ? demand < LeaveAbove && Charge > Floor : demand < EnterBelow && Charge > EnterCharge));
            bool boost = running && !electric && !Dry && aircraft.speed < BoostMaxSpeed
                && (Boost ? inputs.throttle > .85f && Charge > BoostOut : inputs.throttle > .95f && Charge > BoostIn);
            if (electric != Electric || boost != Boost) Apply(electric, boost);
            float rate = Electric ? -(.0015f + .005f * demand) : Boost ? -BoostDrain : running && !Dry ? .012f : 0f;
            Charge = Mathf.Clamp01(Charge + rate * Time.deltaTime);
            if (GameAccess.IsServer() && Time.time >= nextGun) CountShots();
            if (local) Warn();
        }

        // Host, ten times a second: every Halberd round that left since the last look takes its share of the pack (a rearm only resets the count).
        private void CountShots()
        {
            nextGun = Time.time + .1f;
            if (aircraft.weaponStations.Count == 0 || aircraft.weaponStations[0].WeaponInfo != SkylarkWeapons.Halberd) return;
            int ammo = aircraft.weaponStations[0].Ammo;
            if (gunAmmo >= 0 && ammo < gunAmmo)
            {
                int shots = gunAmmo - ammo;
                float before = Charge;
                Charge = Mathf.Clamp01(Charge - shots * ShotCharge);
                GunShots += shots;
                GunDrain += before - Charge;
            }
            gunAmmo = ammo;
        }

        // Once a second: the fuel state and, on the owner, the lift margin on the turbine alone.
        private void Sample(bool local)
        {
            nextSlow = Time.time + 1f;
            bool dry = aircraft.GetFuelQuantity() <= .5f;
            if (dry != Dry)
            {
                Dry = dry;
                if (dry && local && !dryTold) { dryTold = true; SkylarkControl.Say("FUEL OUT: BATTERY ONLY, " + Mathf.RoundToInt(Charge * 100f) + "%"); }
            }
            // Spawn-frame masses are stale (the loadout lands a moment later): judge the lift margin once things settle.
            if (!local || Time.time < born + 3f) return;
            ThrustToWeight = SkylarkTemplate.MaxThrust / (aircraft.GetMass() * 9.81f);
            Heavy = ThrustToWeight < HeavyRatio;
            if (Heavy && !heavyTold && aircraft.radarAlt < 2f)
            {
                heavyTold = true;
                SkylarkControl.Say("HEAVY: T/W " + ThrustToWeight.ToString("0.00") + ", SLOW CLIMB, BOOST AT FULL POWER");
            }
        }

        // Owner peer: one line as the pack passes 25 % and 10 % on the way down.
        private void Warn()
        {
            if (Charge > .3f) { warned = 0; return; }
            int level = Charge < .1f ? 2 : Charge < EnterCharge ? 1 : 0;
            if (level <= warned) return;
            warned = level;
            if (Electric || Boost) SkylarkControl.Say(level == 2 ? "BATTERY 10%" : "BATTERY 25%");
        }

        /// <summary>Client: the host's charge (sent with the drive state), so AUTO picks the same side everywhere.</summary>
        internal void SyncCharge(float charge) => Charge = Mathf.Clamp01(charge);

#if !PUBLIC_RELEASE
        /// <summary>Sim only: put the pack at a given charge (and tell the clients).</summary>
        internal void ForceCharge(float charge)
        {
            Charge = Mathf.Clamp01(charge);
            if (GameAccess.IsServer()) SkylarkNet.Broadcast(aircraft);
        }
#endif

        private void LateUpdate()
        {
            foreach (SkylarkHinge hinge in hinges) hinge.Step(Electric ? 1f : 0f, DoorSeconds, Time.deltaTime);
            if (exhaustGlows != null && exhaustGlows.Length > 0)
            {
                if (exhaustBlock == null) exhaustBlock = new MaterialPropertyBlock();
                bool hot = aircraft.Ignition && !aircraft.disabled && !Electric && !Dry;
                float level = hot ? (Boost ? 1f : Mathf.Lerp(.25f, .72f, Mathf.Clamp01(engine.GetThrustRatio()))) : 0f;
                if (Mathf.Abs(level - shownGlow) > .04f)
                {
                    shownGlow = level;
                    Color baseCol = Color.Lerp(new Color(.05f, .05f, .05f), new Color(.95f, .38f, .08f), level);
                    Color emitCol = new Color(1.4f, .46f, .08f) * level;
                    foreach (Renderer glow in exhaustGlows)
                        if (glow != null) SkylarkMeshKit.SetLamp(glow, exhaustBlock, baseCol, emitCol);
                }
            }
        }

        // The native nozzles and engine recompute heat, sound and fuel flow every step from these limits,
        // so the mode is applied to the limits once per change, never to the per-step results.
        private void Apply(bool electric, bool boost)
        {
            Electric = electric;
            Boost = boost;
            if (electric) Silent.Add(aircraft); else Silent.Remove(aircraft);
            for (int i = 0; i < nozzles.Length; i++)
            {
                if (nozzles[i] == null) continue;
                SkylarkTemplate.Set(nozzles[i], "IRMin", irMin[i] * (electric ? Heat : 1f));
                SkylarkTemplate.Set(nozzles[i], "IRMax", irMax[i] * (electric ? Heat : 1f));
                SkylarkTemplate.Set(nozzles[i], "thrustMaxVolume", fanVolume[i] * (electric ? FanNoise : 1f));
            }
            SkylarkTemplate.Set(engine, "fuelConsumptionMin", electric ? 0f : fuelMin);
            SkylarkTemplate.Set(engine, "fuelConsumptionMax", electric ? 0f : fuelMax);
            // The fans read thrust as a ratio of staticThrust, so the EV cap is a parasitic loss: ratio = dynamic factor - (1 - EvThrust).
            // BOOST is the same lever the other way: a negative loss, and the native duct does not clamp the ratio (ratio = dynamic + BoostGain).
            SkylarkTemplate.Set(engine, "staticThrust", electric ? thrust * EvThrust : thrust);
            SkylarkTemplate.Set(engine, "parasiticThrustLoss", electric ? loss + thrust * EvThrust * (1f - EvThrust) : boost ? loss - thrust * BoostGain : loss);
            if (turbine != null) turbine.volume = turbineVolume * (electric ? TurbineNoise : 1f);
        }

        private void OnDestroy()
        {
            Silent.Remove(aircraft);
            All.Remove(this);
        }

        private static SkylarkDrive LocalDrive()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft mine) || mine == null || mine.disabled) return null;
            if (mine != gaugeAircraft) { gaugeAircraft = mine; gaugeDrive = mine.GetComponent<SkylarkDrive>(); }
            return gaugeDrive;
        }

        /// <summary>The one SKYLARK TAC BOARD gauge: the battery as a bar (ticks at the AUTO entry and the fallback floor), the drive state and C2 as the figure.</summary>
        internal static bool ReadGauge(out GaugeSpec gauge)
        {
            gauge = default;
            SkylarkDrive drive = LocalDrive();
            if (drive == null) return false;
            int percent = Mathf.Clamp(Mathf.RoundToInt(drive.Charge * 100f), 0, 100);
            int word = drive.Dry ? 6 : drive.Boost ? 5 : drive.Heavy && !drive.Electric ? 7 : drive.Mode == DriveMode.Auto ? (drive.Electric ? 0 : 1)
                : drive.Electric ? 2 : drive.Charge < EnterCharge ? 4 : 3;
            bool c2 = drive.mast != null && drive.mast.Deployed;
            int key = word * 2 + (c2 ? 1 : 0);
            bool low = drive.Charge < .15f && (drive.Electric || drive.Boost);
            gauge = new GaugeSpec
            {
                Id = "drive", Label = "SKYLARK", Kind = GaugeKind.Bar, Value = drive.Charge,
                Text = Texts[key] ?? (Texts[key] = Words[word] + (c2 ? " C2" : "")),
                Sub = Percent[percent] ?? (Percent[percent] = "BAT " + percent),
                Tone = low || word >= 6 ? ScopeTone.Amber : ScopeTone.Ink, Flash = low,
                Ticks = Ticks, TickCount = Ticks.Length, TickTone = ScopeTone.Amber, Rank = 30
            };
            return true;
        }
    }

    // Vanilla seekers and the combat AI only ask whether a unit has a heat signature at all; intensity alone
    // changes nothing but flare rejection. On the battery the Skylark has none to offer a seeker at launch.
    [HarmonyPatch(typeof(Unit), nameof(Unit.HasIRSignature))]
    internal static class SkylarkSilentRunningPatch
    {
        private static void Postfix(Unit __instance, ref bool __result)
        {
            if (__result && SkylarkDrive.IsSilent(__instance)) __result = false;
        }
    }

    // The native turbofan only spins while Aircraft.UseFuel says the tanks hold fuel, even for a draw of nothing. On the battery the
    // Skylark draws nothing, so the fans keep turning with dry tanks (and the out-of-fuel warning stays quiet): EV flies it home.
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UseFuel))]
    internal static class SkylarkBatteryFuelPatch
    {
        // After Ares' AresFuelPatch: its fuel cheat zeroes fuelDrawn, which this prefix reads (tools/arch/harmony-shared.json).
        [HarmonyAfter("com.marci.boscalisummer.module.ares")]
        private static bool Prefix(Aircraft __instance, float fuelDrawn, ref bool __result)
        {
            if (fuelDrawn > 0f || !SkylarkDrive.IsSilent(__instance)) return true;
            __result = true;
            return false;
        }
    }
}
