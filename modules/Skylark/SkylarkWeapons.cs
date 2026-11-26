using System;
using System.Collections.Generic;
using System.Linq;
using NuclearOption.SavedMission;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// Stations and loadouts. The order is the mission/network loadout contract: chin turret, L1, R1, L2 (tip), R2, then the appended L3, R3
    /// (mid) and C (belly), each only when the art has its anchor. The chin gun is a native turret (the vanilla 30 mm rotary mount, retuned);
    /// every other store is a vanilla mount. Several options are Vanguard carrier keys (AGM_heavy_single, ARM1_single, ECMPod1), so the
    /// runtime stores (TORCH, MANTA, SUNSPEAR, STORMCELL, WATCHTOWER) are offered here too.
    /// </summary>
    internal static class SkylarkWeapons
    {
        internal const string TurretKey = "BS_Skylark_Turret";
        private const string TurretDonor = "turret_30mm_rotary";
        private static readonly string[] Anchors = { "GunPosition", "Hardpoint_L1", "Hardpoint_R1", "Hardpoint_L2", "Hardpoint_R2", "Hardpoint_L3", "Hardpoint_R3", "Hardpoint_C" };
        private static readonly string[] Names = { "Chin turret", "Left inner station", "Right inner station", "Left tip station", "Right tip station", "Left mid station", "Right mid station", "Belly station" };
        private static readonly string[] Groups = { "Turret", "Inner stations", "Inner stations", "Tip stations", "Tip stations", "Mid stations", "Mid stations", "Belly station" };
        private static readonly string[] Inner = { "AGM1_double", "AGM1_triple", "AGM1_quad", "AGM2_6Pod", "RocketPod1_triple", "Rocket2_4Pod", "bomb_250_glide_single", "AGM_heavy_single", "ARM1_single", "AAM2_single", "AAM3_single" };
        private static readonly string[] Tip = { "AAM1_single", "AAM1_double", "IRMS1_single", "IRMS1_double", "AAM2_single", "AAM3_single" };
        private static readonly string[] Mid = { "AGM1_single", "AGM1_double", "AGM1_triple", "RocketPod1_single", "Rocket2_4Pod", "bomb_125_double", "bomb_250_glide_single", "IRMS1_double", "AAM2_single", "ARM1_single", "ECMPod1" };
        private static readonly string[] Belly = { "gunpod_20mm", "ECMPod1", "JammingPod1", "ARM1_single", "AGM_heavy_single", "bomb_250_glide_single" };
        private static readonly string[][] Options = { null, Inner, Inner, Tip, Tip, Mid, Mid, Belly };
        // Stores per station in the order above (the first slot is always the turret). Keeps external stores under about 1.7 t.
        private static readonly (string Name, float Fuel, string[] Stores)[] Loadouts =
        {
            ("STRIKE", .7f, new[] { null, "AGM1_triple", "AGM1_triple", "AAM1_single", "AAM1_single", "AGM1_double", "AGM1_double", null }),
            ("CAS", .7f, new[] { null, "RocketPod1_triple", "RocketPod1_triple", "AAM1_single", "AAM1_single", "bomb_125_double", "bomb_125_double", "gunpod_20mm" }),
            ("SEAD / OPS", .8f, new[] { null, "ARM1_single", "ARM1_single", "AAM1_single", "AAM1_single", null, null, "ECMPod1" }),
            ("PRECISION", .7f, new[] { null, "bomb_250_glide_single", "bomb_250_glide_single", "AAM1_single", "AAM1_single", "AGM1_double", "AGM1_double", null }),
            ("AIR", .8f, new[] { null, null, null, "AAM1_double", "AAM1_double", "IRMS1_double", "IRMS1_double", null }),
            ("RECON / C2", 1f, new[] { null, null, null, "AAM1_single", "AAM1_single", null, null, "ECMPod1" }),
            ("SILENT", 1f, new[] { null, null, null, "AAM1_single", "AAM1_single", null, null, null }),
            ("INTERCEPT", .85f, new[] { null, "AAM2_single", "AAM2_single", "AAM1_double", "AAM1_double", "IRMS1_double", "IRMS1_double", "ECMPod1" }),
            ("ANTI-ARMOR", .7f, new[] { null, "AGM_heavy_single", "AGM_heavy_single", "AAM1_single", "AAM1_single", "AGM1_double", "AGM1_double", "gunpod_20mm" })
        };

        internal const string SilentLoadout = "SILENT";

        /// <summary>The standard loadout the aircraft carries (by stores, so a reloaded copy matches too), or null for a hand-built one.</summary>
        internal static string LoadoutName(Aircraft aircraft)
        {
            Loadout carried = aircraft.loadout;
            StandardLoadout[] standard = aircraft.definition?.aircraftParameters?.StandardLoadouts;
            if (carried?.weapons == null || standard == null) return null;
            foreach (StandardLoadout s in standard)
                if (s.loadout == carried || s.loadout?.weapons != null && s.loadout.weapons.SequenceEqual(carried.weapons)) return s.Name;
            return null;
        }

        /// <summary>The chin turret's mount; registered in the Encyclopedia with the aircraft.</summary>
        internal static WeaponMount TurretMount { get; private set; }

        /// <summary>The aircraft's chin turret. Damage parts leave the root's hierarchy at spawn, so it is found through its weapon station.</summary>
        internal static Turret FindTurret(Aircraft aircraft) =>
            aircraft.weaponStations.Count > 0 && aircraft.weaponStations[0].Weapons.Count > 0 ? aircraft.weaponStations[0].Weapons[0].GetComponentInParent<Turret>(true) : null;

        internal static void Refit(Aircraft aircraft, AircraftParameters parameters, Dictionary<string, Transform> nodes, Transform holder, List<WeaponMount> mounts)
        {
            WeaponMount Find(string key)
            {
                WeaponMount mount = mounts.FirstOrDefault(m => m != null && (m.jsonKey == key || m.name == key));
                if (mount == null) Debug.LogWarning("[Skylark] weapon mount absent, option skipped: " + key);
                return mount;
            }

            TurretMount = BuildTurret(holder, nodes, Find(TurretDonor));
            int[] present = Enumerable.Range(0, Anchors.Length).Where(i => nodes.ContainsKey(Anchors[i])).ToArray();
            if (present.Length < Anchors.Length) Debug.Log("[Skylark] stations without art anchors, not offered: " + string.Join(", ", Enumerable.Range(0, Anchors.Length).Except(present).Select(i => Names[i])));
            var sets = new HardpointSet[present.Length];
            for (int n = 0; n < sets.Length; n++)
            {
                int i = present[n];
                Transform anchor = nodes[Anchors[i]];
                anchor.rotation = Quaternion.identity;
                var hp = new Hardpoint { transform = anchor, part = anchor.GetComponentInParent<UnitPart>(true), bayDoors = Array.Empty<BayDoor>(), BuiltInWeapons = Array.Empty<Weapon>(), BuiltInTurrets = Array.Empty<Turret>() };
                SkylarkTemplate.EmptyArray(hp, "pylonOptions");
                var options = new List<WeaponMount>();
                if (i == 0) options.Add(TurretMount);
                else
                {
                    options.Add(null);
                    options.AddRange(Options[i].Select(Find).Where(m => m != null));
                }
                sets[n] = new HardpointSet
                {
                    name = Names[i], precludingHardpointSets = new List<byte>(), SymmetryWithPrev = i == 2 || i == 4 || i == 6,
                    SymmetryName = Groups[i], hardpoints = new List<Hardpoint> { hp }, weaponOptions = options
                };
            }
            aircraft.weaponManager.hardpointSets = sets;
            var loadouts = Loadouts.Select(l => new Loadout { weapons = present.Select(i => i == 0 ? TurretMount : l.Stores[i] == null ? null : Find(l.Stores[i])).ToList() }).ToList();
            parameters.loadouts = loadouts;
            parameters.StandardLoadouts = Loadouts.Select((l, k) => new StandardLoadout { Name = l.Name, loadout = loadouts[k], FuelRatio = l.Fuel }).ToArray();
        }

        /// <summary>The EMC-3 Halberd's shell: a clone of the donor 30 mm's, retuned (the donor asset is never edited).</summary>
        internal static WeaponInfo Halberd { get; private set; }

        // Halberd ballistics. Hit damage is (pierce * (v / muzzle)^2 - armour) / tolerance, so 340 at the muzzle kills trucks (armour 50),
        // light armour (100-175) and helicopters (60-120) but stays under every tank part (350 and up); blast is a token spark.
        private const float Muzzle = 1600f, Pierce = 340f, Blast = .02f, Visibility = 600f, Spread = .04f;
        internal const int Rounds = 300;

        private static WeaponMount BuildTurret(Transform holder, Dictionary<string, Transform> nodes, WeaponMount donor)
        {
            if (donor == null) throw new InvalidOperationException("Turret donor mount is absent: " + TurretDonor);
            WeaponMount mount = Object.Instantiate(donor);
            mount.name = TurretKey;
            mount.jsonKey = TurretKey;
            // The donor is vehicle or event content: a clone inheriting that is refused by every HQ and loadout check.
            SkylarkTemplate.Set(mount, "disabled", false);
            SkylarkTemplate.Set(mount, "isEventContent", false);
            Halberd = Object.Instantiate(mount.info);
            Halberd.name = "WI_BS_Skylark_Halberd";
            Halberd.weaponName = "EMC-3 Halberd";
            Halberd.shortName = "COIL";
            Halberd.description = "Recessed chin coilgun with 1600 m/s slugs and no spin-up. Quiet on the battery (EV), overdrive on the turbine (HYBRID).";
            Halberd.muzzleVelocity = Muzzle;
            Halberd.pierceDamage = Pierce;
            Halberd.blastDamage = Blast;
            Halberd.visibilityWhenFired = Visibility;
            Halberd.maxSpeed = Muzzle;
            mount.info = Halberd;
            mount.prefab = Object.Instantiate(mount.prefab, holder);
            mount.prefab.name = TurretKey;
            mount.prefab.transform.localPosition = Vector3.zero;
            mount.prefab.transform.localRotation = Quaternion.identity;
            foreach (MeshRenderer renderer in mount.prefab.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = false;
            // The donor's collision hulls would sit inside the airframe and tear the nose off at spawn.
            foreach (Collider collider in mount.prefab.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            Gun cannon = mount.prefab.GetComponentInChildren<Gun>(true);
            Turret turret = mount.prefab.GetComponentInChildren<Turret>(true);
            cannon.info = Halberd;
            SkylarkTemplate.Set(cannon, "magazineCapacity", Rounds);
            SkylarkTemplate.Set(cannon, "magazines", 0);
            SkylarkTemplate.Set(cannon, "fireRate", SkylarkTurret.HybridRate);
            SkylarkTemplate.Set(cannon, "bulletSpread", Spread);
            SkylarkTemplate.Set(cannon, "tracerRatio", 1);
            SkylarkTemplate.Set(cannon, "tracerSize", .55f);
            SkylarkTemplate.Set(cannon, "tracerColor", SkylarkTurret.Tracer);
            SkylarkTemplate.Set(cannon, "spinRate", 0f);
            SkylarkTemplate.Set(cannon, "pitch", 1.3f);
            // The donor's barrel heat would reset the drive-scheduled rate every frame (Heat.Update restores its own base rate).
            SkylarkTemplate.Set(cannon, "heatEnabled", false);
            // No propellant, no smoke: only the donor's flash plays (tinted cyan by SkylarkTurret).
            SkylarkTemplate.Set(cannon, "muzzleParticles", (SkylarkTemplate.Get<ParticleSystem[]>(cannon, "muzzleParticles") ?? Array.Empty<ParticleSystem>())
                .Where(p => p != null && p.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) < 0).ToArray());
            // Traverse +-110 degrees, elevation 60 down to 15 up; the donor's 120 degrees at 40 deg/s is a helicopter's.
            SkylarkTemplate.Set(turret, "traverseRange", 110f);
            SkylarkTemplate.Set(turret, "traverseRate", 70f);
            SkylarkTemplate.Set(turret, "elevationRate", 45f);
            SkylarkTemplate.Set(turret, "minElevation", -60f);
            SkylarkTemplate.Set(turret, "maxElevation", 15f);
            if (nodes.TryGetValue("GunMuzzle", out Transform tip) && nodes.TryGetValue("GunPosition", out Transform ring))
            {
                Vector3 origin = mount.prefab.transform.position - ring.position;
                if (nodes.TryGetValue("GunTurret_Yaw", out Transform yaw) && nodes.TryGetValue("GunTurret_Pitch", out Transform pitch))
                {
                    turret.transform.position = origin + yaw.position;
                    Transform elevation = SkylarkTemplate.Get<Transform>(turret, "elevationTransform");
                    // The donor leaves its elevation pivot unset and Turret.Awake uses the gun transform.
                    if (elevation == null)
                    {
                        elevation = cannon.transform;
                        SkylarkTemplate.Set(turret, "elevationTransform", elevation);
                    }
                    elevation.position = origin + pitch.position;
                }
                Transform muzzle = SkylarkTemplate.Empty("SkylarkMuzzle", cannon.transform, origin + tip.position);
                SkylarkTemplate.Set(cannon, "muzzles", new[] { muzzle });
                foreach (ParticleSystem particles in SkylarkTemplate.Get<ParticleSystem[]>(cannon, "muzzleParticles") ?? Array.Empty<ParticleSystem>()) particles.transform.position = muzzle.position;
            }
            else Debug.Log("[Skylark] GunMuzzle absent: the turret fires from its donor muzzle");
            mount.emptyMass = 120f;
            mount.emptyDrag = .005f;
            mount.Initialize();
            mount.mountName = "EMC-3 Halberd coilgun";
            mount.mass = mount.emptyMass + mount.ammo * mount.info.massPerRound;
            mount.drag = mount.emptyDrag;
            return mount;
        }
    }

    /// <summary>
    /// The EMC-3 Halberd on every peer, with no wire of its own. It copies the native turret's traverse and elevation onto the authored
    /// GunTurret_Yaw / GunTurret_Pitch nodes, folded flush and centred while another weapon is selected; it sets the fire rate from the
    /// replicated drive (EV 300 rpm, quiet and dim; HYBRID 550 rpm, the turbine feeding the coils); and on every round, read from the
    /// gun's own ammo count, it kicks the optional GunBarrel back, ripples the GunCoil0..5 collars breech to muzzle and flashes GunGlow.
    /// </summary>
    internal sealed class SkylarkTurret : MonoBehaviour
    {
        internal const float EvRate = 300f, HybridRate = 550f;
        internal static readonly Color Tracer = new Color(.25f, 1.6f, 2.6f);
        private const float Recoil = .035f, RecoilReturn = .09f, RippleSeconds = .1f, GlowSeconds = .05f, DeploySeconds = .25f, EvVolume = .45f;
        private const float DoorOpenAngle = 72f;
        private static Material coilLit;
        [SerializeField] private Transform yaw, pitch, barrel;
        [SerializeField] private Transform[] doors;
        [SerializeField] private bool hasDoors;
        [SerializeField] private float stowPitch;
        [SerializeField] private Renderer[] coils;
        [SerializeField] private Renderer glow;
        private Material[] coilRest;
        private bool[] coilOn;
        private Material glowRest;
        private Light glowLight;
        private Quaternion yawRest, pitchRest;
        private SkylarkHinge[] doorHinges;
        private MaterialPropertyBlock armedBlock;
        private Vector3 yawAxis, pitchAxis, barrelPosition, barrelAxis;
        private Turret turret;
        private Transform elevation;
        private Gun gun;
        private Aircraft aircraft;
        private SkylarkDrive drive;
        private AudioSource[] sources;
        private float[] volumes;
        private ParticleSystem[] flashes;
        private float nextSearch, kick, ripple = 1f, flash, deploy;
        private int lastAmmo = -1, armedState = -1;
        private bool electric, tuned, glowOn;

        /// <summary>Rounds seen leaving the muzzle, coil ripples started, the current rate (rpm) and the fold (0 stowed, 1 deployed); sim readback.</summary>
        internal int Shots { get; private set; }
        internal int Ripples { get; private set; }
        internal float Rate { get; private set; }
        internal float Deploy => deploy;
        internal int CoilCount => coils != null ? coils.Length : 0;
        internal bool HasBayDoors => hasDoors;

#if !PUBLIC_RELEASE
        internal (float Gap, float Angle) MuzzleFit()
        {
            if (pitch == null || gun == null) return (-1f, -1f);
            Transform visual = SkylarkTemplate.Node(pitch, "GunMuzzle");
            Transform[] muzzles = SkylarkTemplate.Get<Transform[]>(gun, "muzzles");
            if (visual == null || muzzles == null || muzzles.Length == 0 || muzzles[0] == null) return (-1f, -1f);
            Transform native = muzzles[0];
            return (Vector3.Distance(native.position, visual.position), Vector3.Angle(native.forward, visual.position - pitch.position));
        }
#endif

        internal void Configure(Dictionary<string, Transform> nodes)
        {
            yaw = nodes["GunTurret_Yaw"];
            pitch = nodes["GunTurret_Pitch"];
            barrel = nodes.TryGetValue("GunBarrel", out Transform b) ? b : null;
            doors = new[] { nodes.TryGetValue("GunBayDoor_R", out Transform dr) ? dr : null,
                            nodes.TryGetValue("GunBayDoor_L", out Transform dl) ? dl : null };
            hasDoors = dr != null || dl != null;
            stowPitch = nodes.ContainsKey("ChinFairing") ? -19f : -8f;
            coils = Enumerable.Range(0, 6).Select(i => nodes.TryGetValue("GunCoil" + i, out Transform c) ? c.GetComponent<Renderer>() : null).Where(r => r != null).ToArray();
            glow = nodes.TryGetValue("GunGlow", out Transform g) ? g.GetComponent<Renderer>() : null;
            if (coils.Length > 0 || glow != null) CoilMaterial();
        }

        // The native gear lamp's lit material, tinted cyan: an emissive the game already draws, shared by every Skylark (never destroyed).
        private static void CoilMaterial()
        {
            if (coilLit != null || SkylarkLights.WhiteLit == null) return;
            coilLit = new Material(SkylarkLights.WhiteLit) { name = "BS_Skylark_CoilLit" };
            var tinted = new List<string>();
            foreach (string property in new[] { "_EmissionColor", "_Color", "_BaseColor", "_TintColor" })
                if (coilLit.HasProperty(property)) { coilLit.SetColor(property, Tracer); tinted.Add(property); }
            Debug.Log("[Skylark] Halberd coil glow on " + coilLit.shader.name + ", tinted " + string.Join(",", tinted));
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            drive = GetComponent<SkylarkDrive>();
            yawRest = yaw.localRotation;
            pitchRest = pitch.localRotation;
            yawAxis = yaw.parent.InverseTransformDirection(transform.up);
            pitchAxis = pitch.parent.InverseTransformDirection(transform.right);
            doorHinges = new SkylarkHinge[doors != null ? doors.Length : 0];
            for (int i = 0; i < doorHinges.Length; i++)
                if (doors[i] != null) doorHinges[i] = new SkylarkHinge(transform, doors[i], Vector3.forward, i == 0 ? -DoorOpenAngle : DoorOpenAngle);
            coilRest = coils.Select(r => r.sharedMaterial).ToArray();
            coilOn = new bool[coils.Length];
            armedBlock = new MaterialPropertyBlock();
            if (glow != null)
            {
                glowRest = glow.sharedMaterial;
                glowLight = new GameObject("SkylarkHalberdGlow").AddComponent<Light>();
                glowLight.transform.SetParent(glow.transform, false);
                glowLight.type = LightType.Point;
                glowLight.color = new Color(.3f, .85f, 1f);
                glowLight.range = 2.5f * SkylarkTemplate.Scale;
                glowLight.shadows = LightShadows.None;
                glowLight.enabled = false;
            }
            if (barrel == null) return;
            barrelPosition = barrel.localPosition;
            // Authored at rest: the barrel's length runs along the aircraft's nose axis; it recoils straight back along it.
            barrelAxis = barrel.parent.InverseTransformDirection(transform.forward).normalized;
        }

        private bool Find()
        {
            if (turret != null) return true;
            if (Time.time < nextSearch) return false;
            nextSearch = Time.time + 1f;
            turret = SkylarkWeapons.FindTurret(aircraft);
            gun = turret != null ? aircraft.weaponStations[0].Weapons[0] as Gun : null;
            if (gun == null) { turret = null; return false; }
            elevation = SkylarkTemplate.Get<Transform>(turret, "elevationTransform");
            sources = gun.GetComponents<AudioSource>();
            volumes = sources.Select(s => s.volume).ToArray();
            flashes = SkylarkTemplate.Get<ParticleSystem[]>(gun, "muzzleParticles") ?? Array.Empty<ParticleSystem>();
            lastAmmo = gun.ammo;
            tuned = false;
            return true;
        }

        // The coils' rate follows the drive every peer sees: on the battery alone they cycle slower, quieter and dimmer.
        private void Tune()
        {
            bool ev = drive != null && drive.Electric;
            if (tuned && ev == electric) return;
            tuned = true;
            electric = ev;
            Rate = ev ? EvRate : HybridRate;
            SkylarkTemplate.Set(gun, "fireRate", Rate);
            SkylarkTemplate.Set(gun, "fireInterval", 60f / Rate);
            for (int i = 0; i < sources.Length; i++) if (sources[i] != null) sources[i].volume = volumes[i] * (ev ? EvVolume : 1f);
            foreach (ParticleSystem particles in flashes)
            {
                if (particles == null) continue;
                ParticleSystem.MainModule main = particles.main;
                main.startColor = ev ? new Color(.35f, .85f, 1f, .35f) : new Color(.55f, .95f, 1f, 1f);
            }
            if (glowLight != null) glowLight.intensity = ev ? .6f : 1.8f;
        }

        private void LateUpdate()
        {
            if (yaw == null || pitch == null || !Find()) return;
            Tune();
            int ammo = gun.ammo;
            bool fired = ammo < lastAmmo;
            if (fired)
            {
                Shots += lastAmmo - ammo;
                Ripples++;
                kick = Recoil;
                ripple = 0f;
                flash = GlowSeconds;
            }
            lastAmmo = ammo;
            float dt = Time.deltaTime;
            float yawAngle = Mathf.DeltaAngle(0f, turret.transform.localEulerAngles.y), pitchAngle = Mathf.DeltaAngle(0f, elevation.localEulerAngles.x);
            // Aircraft.SetActiveStation already replicates selection to observers. Native aim can remain off-axis
            // after deselection. If it fires during the fold, put the barrel on the firing axis for the flash.
            bool selected = !aircraft.disabled && aircraft.weaponStations.Count > 0
                && aircraft.weaponManager.currentWeaponStation == aircraft.weaponStations[0];
            deploy = fired ? 1f : Mathf.MoveTowards(deploy, selected ? 1f : 0f, dt / DeploySeconds);
            // The bare gimbal begins its spring return at once. Blister doors on older bundles open first.
            float fold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(hasDoors ? (deploy - .2f) / .8f : deploy));
            yaw.localRotation = Quaternion.AngleAxis(yawAngle * fold, yawAxis) * yawRest;
            pitch.localRotation = Quaternion.AngleAxis(Mathf.Lerp(stowPitch, pitchAngle, fold), pitchAxis) * pitchRest;
            float doorPhase = Mathf.Clamp01(deploy / .25f);
            if (doorHinges != null)
                foreach (SkylarkHinge hinge in doorHinges)
                    if (hinge != null) hinge.Set(doorPhase);
            if (barrel != null && (kick > 0f || barrel.localPosition != barrelPosition))
            {
                kick = Mathf.MoveTowards(kick, 0f, dt * Recoil / RecoilReturn);
                barrel.localPosition = barrelPosition - barrelAxis * kick;
            }
            if (ripple < 1f || coilOn.Any(o => o))
            {
                ripple = Mathf.Min(1f, ripple + dt / RippleSeconds);
                for (int i = 0; i < coils.Length; i++)
                {
                    bool on = ripple < 1f && Mathf.Abs(ripple * coils.Length - i - .5f) < 1f;
                    if (on == coilOn[i] || coils[i] == null) continue;
                    coilOn[i] = on;
                    if (i == 0 && on) { armedBlock.Clear(); coils[0].SetPropertyBlock(armedBlock); armedState = -1; }
                    coils[i].sharedMaterial = on ? coilLit : coilRest[i];
                }
            }
            UpdateArmedStandby(selected && !aircraft.disabled && aircraft.Ignition);
            flash -= dt;
            bool lit = flash > 0f;
            if (glow != null && lit != glowOn)
            {
                glowOn = lit;
                glow.sharedMaterial = lit && coilLit != null ? coilLit : glowRest;
                glowLight.enabled = lit;
            }
        }

        private void UpdateArmedStandby(bool armed)
        {
            if (coils == null || coils.Length == 0 || coils[0] == null || armedBlock == null || coilOn[0]) return;
            int nextState = !armed ? 0 : drive != null && drive.Charge < SkylarkDrive.Floor ? 3 : electric ? 1 : 2;
            if (nextState == armedState) return;
            armedState = nextState;
            if (nextState == 0)
            {
                armedBlock.Clear();
                coils[0].SetPropertyBlock(armedBlock);
                return;
            }
            Color glowColor = nextState == 3 ? new Color(1.1f, .45f, .08f)
                : nextState == 1 ? new Color(.08f, .45f, .70f)
                : new Color(.22f, 1.05f, 1.45f);
            armedBlock.SetColor("_EmissionColor", glowColor);
            coils[0].SetPropertyBlock(armedBlock);
        }
    }

    // Owner peer: the coils fire from the traction battery. Below the floor the trigger does nothing and the pilot is told why.
    [HarmonyLib.HarmonyPatch(typeof(Gun), nameof(Gun.Fire))]
    internal static class SkylarkCoilsDryPatch
    {
        private static float nextSay;
        /// <summary>Trigger pulls refused for a flat battery (sim readback).</summary>
        internal static int Refused { get; private set; }

        private static bool Prefix(Gun __instance)
        {
            if (__instance.info != SkylarkWeapons.Halberd || __instance.attachedUnit == null || !__instance.attachedUnit.LocalSim) return true;
            SkylarkDrive drive = __instance.attachedUnit.GetComponent<SkylarkDrive>();
            if (drive == null || drive.Charge >= SkylarkDrive.Floor) return true;
            Refused++;
            if (Time.time >= nextSay && GameManager.IsLocalAircraft(__instance.attachedUnit as Aircraft))
            {
                nextSay = Time.time + 2f;
                SkylarkControl.Say("COILS DRY: BATTERY " + Mathf.RoundToInt(drive.Charge * 100f) + "%");
            }
            return false;
        }
    }
}
