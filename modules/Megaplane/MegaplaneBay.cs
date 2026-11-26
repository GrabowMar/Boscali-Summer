using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BoscaliSummer.Core.Contracts;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    internal sealed class MegaplaneSettings
    {
        public ConfigEntry<string> BayFighter { get; }
        public ConfigEntry<KeyboardShortcut> DropFighterKey { get; }
        public ConfigEntry<KeyboardShortcut> RecallFighterKey { get; }
        public ConfigEntry<string> BayPayload { get; }

        public MegaplaneSettings(ConfigFile config)
        {
            const string S = "Megaplane";
            BayFighter = config.Bind(S, "BayFighter", "Fighter1",
                "Fighter carried on the KY-44's two belly trapezes: a prefab name, unit key or unit name (Fighter1 = FS-12 Revoker, SmallFighter1 = FS-20 Vortex). Needs a mission restart.");
            DropFighterKey = config.Bind(S, "DropFighterKey", new KeyboardShortcut(KeyCode.Insert),
                "While flying the KY-44: open the belly bay, swing the next fighter down on its trapeze and release it as a live AI aircraft of your side.");
            BayPayload = config.Bind(S, "BayPayload", "Fighters",
                new ConfigDescription("What the KY-44's belly bay carries: Fighters (two parasite fighters on trapezes) or Railgun (the RG-240 Trident on a lowering mount). Every player must use the same choice. Needs a game restart.",
                    new AcceptableValueList<string>("Fighters", "Railgun")));
            RecallFighterKey = config.Bind(S, "RecallFighterKey", new KeyboardShortcut(KeyCode.Delete),
                "While flying the KY-44: recall the released bay fighters. The bay opens and lowers its empty trapezes; the carrier's datalink flies each fighter to a gate behind and below the bay, then up onto the hook (an automated FICON / DARPA Gremlins style capture). Hold course and speed while they dock.");
        }

        internal static MegaplaneSettings Current;
        internal static bool Railgun => Current != null && Current.BayPayload.Value == "Railgun";
    }

    /// <summary>Belly fighter bay of the KY-44 (build_megaplane.py fighter_bay): two parasite fighters hang on FICON-style
    /// trapezes. The template carries a render-only copy of the chosen fighter on each yoke; a drop opens the door pairs,
    /// swings the trapeze down 90 deg below the belly and, on the host, spawns that fighter as a real AI aircraft of the
    /// carrier's side at the copy's pose and the carrier's velocity, then retracts and closes.</summary>
    internal static class MegaplaneBayTemplate
    {
        internal static readonly string[] Slots = { "F", "A" };
        private static readonly Regex Hidden = new Regex("gear|wheel|strut|lg_|tyre|tire|damage|destroy|broken|debris|pilot|ladder|chock", RegexOptions.IgnoreCase);

        internal static AircraftDefinition Pick(Encyclopedia encyclopedia)
        {
            string want = MegaplaneSettings.Current?.BayFighter.Value ?? "Fighter1";
            bool Is(AircraftDefinition d, string s) => d.unitPrefab != null && (d.unitPrefab.name == s || d.jsonKey == s || d.unitName == s);
            return encyclopedia.aircraft.FirstOrDefault(d => d != null && Is(d, want))
                ?? encyclopedia.aircraft.FirstOrDefault(d => d != null && Is(d, "Fighter1"));
        }

        internal static void Build(Encyclopedia encyclopedia, Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            AircraftDefinition fighter = Pick(encyclopedia);
            if (fighter == null) { Debug.LogWarning("[Megaplane] no bay fighter found"); return; }
            foreach (string key in Slots)
                if (nodes.TryGetValue("FighterSlot_" + key, out Transform slot)) Visual(fighter.unitPrefab, slot, key);
            aircraft.gameObject.AddComponent<MegaplaneBay>().FighterKey = fighter.jsonKey;
        }

        private static bool Shown(Transform t, Transform root)
        {
            for (; t != null; t = t == root ? null : t.parent)
                if (!t.gameObject.activeSelf || Hidden.IsMatch(t.name)) return false;
            return true;
        }

        /// <summary>Render-only copy (top level of detail, gear hidden) hung with its top-centre on the hook point.</summary>
        private static void Visual(GameObject prefab, Transform slot, string key)
        {
            var excluded = new HashSet<Renderer>();
            foreach (LODGroup g in prefab.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++) foreach (Renderer r in lods[i].renderers) if (r != null) excluded.Add(r);
            }
            // The parked copy has no running engine to turn off the donor's afterburner renderers.
            var afterburners = AccessTools.Field(typeof(JetNozzle), "afterburners");
            if (afterburners?.FieldType.GetElementType() is System.Type stageType)
            {
                var flame = AccessTools.Field(stageType, "flameRenderer");
                var glow = AccessTools.Field(stageType, "nozzleGlowRenderer");
                foreach (JetNozzle nozzle in prefab.GetComponentsInChildren<JetNozzle>(true))
                {
                    if (!(afterburners.GetValue(nozzle) is System.Array stages)) continue;
                    foreach (object stage in stages)
                    {
                        if (stage == null) continue;
                        if (flame?.GetValue(stage) is Renderer fire && fire != null) excluded.Add(fire);
                        if (glow?.GetValue(stage) is Renderer light && light != null) excluded.Add(light);
                    }
                }
            }
            var root = new GameObject("BayFighter_" + key).transform;
            root.SetParent(slot, false);
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            var bounds = new Bounds();
            bool any = false;
            foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (excluded.Contains(r) || !r.enabled || mf == null || mf.sharedMesh == null || !Shown(r.transform, prefab.transform)) continue;
                Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                var go = new GameObject(r.name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = m.GetColumn(3);
                go.transform.localRotation = m.rotation;
                go.transform.localScale = m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                Bounds b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) * 2 - 1, (c & 2) - 1, (c & 4) / 2 - 1)));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            root.localPosition = -new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
    }

    internal sealed class MegaplaneBay : MonoBehaviour
    {
        [SerializeField] internal string FighterKey;
        private const float DoorOpen = 100f;   // degrees: the door leaves swing down and outboard
        private const float DoorRate = 35f, ArmRate = 22f;   // deg/s: the doors lead, the arms follow
        private const float DeckHoldTime = 1.25f, LaunchWait = 20f;
        private Aircraft carrier;
        private int arrivingSlot = -1;
        private float deckSteadyTime, launchDeadline;
        private Quaternion lastDeckRotation;
        private Vector3 remoteTurnRate;
        private bool sampledDeck;
        private bool predictedDeckReady;
        private GlobalPosition predictedDeckPosition;
        private Quaternion predictedDeckRotation;
        private Vector3 predictedDeckVelocity, positionCorrection, predictedTurnRate;
        private readonly List<(Transform t, Quaternion rest, float sign)> doors = new List<(Transform, Quaternion, float)>();
        private readonly Transform[] arms = new Transform[2], yokes = new Transform[2], visuals = new Transform[2];
        private readonly float[] armL = new float[2];   // authored arm length, read from the art: the ×1.246 build scale moved it past any constant
        internal readonly float[] Arm = new float[2];
        internal readonly int[] State = new int[2];   // 0 stowed, 1 dropping, 2 released (empty), 3 waiting to catch, 4 stowing a caught fighter
        internal readonly float[] ReadyAt = new float[2];   // a recovered fighter is refuelled and rearmed before it can drop again
        internal const float RearmSeconds = 90f;
        internal Aircraft Carrier => carrier;
        internal float Door { get; private set; }
        internal Aircraft[] Released { get; } = new Aircraft[2];
        internal string DeckHoldReason => UnsafeDeckReason ?? (deckSteadyTime < DeckHoldTime ? "HOLD COURSE AND SPEED" : null);
        internal bool DeckReady => DeckHoldReason == null;
        internal bool CanDrop => DeckReady && BayClearForLaunch && (LaunchReady(0) || LaunchReady(1));

        private string UnsafeDeckReason
        {
            get
            {
                if (carrier == null || carrier.disabled || carrier.rb == null || carrier.NetworkHQ == null)
                    return "BAY UNAVAILABLE";
                if (carrier.gearState != LandingGear.GearState.LockedRetracted) return "RAISE LANDING GEAR";
                if (carrier.radarAlt < 250f) return "CLIMB ABOVE 250 M AGL";
                Vector3 velocity = carrier.rb.velocity;
                float forward = Vector3.Dot(velocity, transform.forward);
                if (!(forward >= 95f && forward <= 175f)) return "HOLD 95–175 M/S";
                bool steady = Mathf.Abs(velocity.y) < 7f &&
                    Vector3.Angle(transform.up, Vector3.up) < 13f &&
                    DeckTurnRate.sqrMagnitude < .07f * .07f;
                return steady ? null : "HOLD COURSE AND SPEED";
            }
        }

        private Vector3 DeckTurnRate => carrier.LocalSim ? carrier.rb.angularVelocity : remoteTurnRate;
        private bool UsePredictedDeck => !carrier.LocalSim && predictedDeckReady;
        internal Quaternion DeckRotation => UsePredictedDeck ? predictedDeckRotation : carrier.rb.rotation;
        private Vector3 DeckPosition => UsePredictedDeck ? predictedDeckPosition.ToLocalPosition() : carrier.rb.position;

        internal Vector3 DeckLocal(Vector3 point) => Quaternion.Inverse(DeckRotation) * (point - DeckPosition);

        internal Vector3 DeckVelocity(Vector3 point)
        {
            Rigidbody deck = carrier.rb;
            // A turn swings the near deck. Limit this feed-forward at marshal distances.
            Vector3 center = UsePredictedDeck ? DeckPosition + DeckRotation * deck.centerOfMass : deck.worldCenterOfMass;
            Vector3 offset = Vector3.ClampMagnitude(point - center, 400f);
            Vector3 velocity = UsePredictedDeck ? predictedDeckVelocity + positionCorrection : deck.velocity;
            return velocity + Vector3.Cross(UsePredictedDeck ? predictedTurnRate : DeckTurnRate, offset);
        }

        private void FixedUpdate()
        {
            if (carrier == null || carrier.LocalSim || carrier.rb == null)
            {
                predictedDeckReady = false;
                return;
            }

            Rigidbody deck = carrier.rb;
            var reportedPosition = deck.position.ToGlobalPosition();
            if (!predictedDeckReady || (reportedPosition - predictedDeckPosition).sqrMagnitude > 200f * 200f)
            {
                predictedDeckPosition = reportedPosition;
                predictedDeckRotation = deck.rotation;
                predictedDeckVelocity = deck.velocity;
                positionCorrection = predictedTurnRate = Vector3.zero;
                predictedDeckReady = true;
                return;
            }

            // Remote aircraft snapshots can jump several metres forward or back between fixed steps.
            // Advance using replicated velocity and correct softly towards their reported pose.
            float dt = Time.fixedDeltaTime;
            predictedDeckVelocity = Vector3.Lerp(predictedDeckVelocity, deck.velocity, 1f - Mathf.Exp(-8f * dt));
            GlobalPosition next = predictedDeckPosition + predictedDeckVelocity * dt;
            Vector3 correction = Vector3.ClampMagnitude((reportedPosition - next) * 1.6f, 11f);
            positionCorrection = Vector3.Lerp(positionCorrection, correction, 1f - Mathf.Exp(-4f * dt));
            predictedDeckPosition = next + positionCorrection * dt;

            Quaternion previous = predictedDeckRotation;
            predictedDeckRotation = Quaternion.Slerp(previous, deck.rotation, 1f - Mathf.Exp(-8f * dt));
            predictedTurnRate = AngularChange(previous, predictedDeckRotation, dt);
        }

        private void SampleDeckRotation(float dt)
        {
            Quaternion rotation = transform.rotation;
            if (!carrier.LocalSim && sampledDeck && dt > 0f)
            {
                // Native aircraft snapshots fill rb.velocity but clear rb.angularVelocity for remote pilots.
                Vector3 sample = AngularChange(lastDeckRotation, rotation, dt);
                remoteTurnRate = Vector3.Lerp(remoteTurnRate, sample, 1f - Mathf.Exp(-8f * dt));
            }
            sampledDeck = true;
            lastDeckRotation = rotation;
        }

        private static Vector3 AngularChange(Quaternion before, Quaternion after, float dt)
        {
            (after * Quaternion.Inverse(before)).ToAngleAxis(out float angle, out Vector3 axis);
            float delta = Mathf.DeltaAngle(0f, angle);
            return Mathf.Abs(delta) > .001f ? axis * (delta * Mathf.Deg2Rad / dt) : Vector3.zero;
        }

        private bool BayClearForLaunch => State[0] != 1 && State[1] != 1 && State[0] < 3 && State[1] < 3;
        private bool LaunchReady(int slot) => State[slot] == 0 && Time.time >= ReadyAt[slot] && HasMount(slot);
        internal bool HasMount(int slot) => arms[slot] != null && yokes[slot] != null && visuals[slot] != null &&
            visuals[slot].IsChildOf(transform);

        internal bool CanRecall(int slot)
        {
            if (carrier == null || !carrier.IsServer || carrier.disabled || carrier.rb == null ||
                State[0] == 1 || State[1] == 1 || State[slot] != 2 || !HasMount(slot)) return false;
            Aircraft fighter = Released[slot];
            // An aircraft still clearing the bay stays with departure control until it is clear.
            return MegaplaneCapture.CanGuide(fighter) && fighter.NetworkHQ == carrier.NetworkHQ &&
                !fighter.TryGetComponent(out MegaplaneCapture _);
        }

        private void Awake()
        {
            carrier = GetComponent<Aircraft>();
            Dictionary<string, Transform> all = GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach (KeyValuePair<string, Transform> kv in all.Where(kv => kv.Key.StartsWith("FighterDoor_")))
                doors.Add((kv.Value, kv.Value.localRotation, kv.Key.EndsWith("_L") ? -1f : 1f));
            for (int i = 0; i < 2; i++)
            {
                string k = MegaplaneBayTemplate.Slots[i];
                all.TryGetValue("TrapezeArm_" + k, out arms[i]);
                all.TryGetValue("TrapezeYoke_" + k, out yokes[i]);
                all.TryGetValue("BayFighter_" + k, out visuals[i]);
                if (visuals[i] == null) State[i] = 2;
                else visuals[i].gameObject.SetActive(State[i] != 2 && State[i] != 3);
                // the authored rest pose carries the arm length (design 9 m, built 11.2 after the span pass): read it,
                // so the yoke and its fighter always ride the arm tip exactly where the art hangs them
                armL[i] = arms[i] != null && yokes[i] != null ? Vector3.Distance(yokes[i].localPosition, arms[i].localPosition) : 9f;
            }
            MegaplaneNet.Register(this);
        }

        private void OnDestroy()
        {
            foreach (Aircraft fighter in Released)
                if (fighter != null && fighter.TryGetComponent(out MegaplaneCapture capture)) capture.Cancel();
            MegaplaneNet.Unregister(this);
        }

        /// <summary>Host: one slot changed state — tell every peer, and let the visual follow the state.</summary>
        private void SetSlot(int slot, int value)
        {
            State[slot] = value;
            if (visuals[slot] != null) visuals[slot].gameObject.SetActive(value != 2 && value != 3);
            if (carrier != null && carrier.IsServer)
                MegaplaneNet.Broadcast(carrier.persistentID.Id, slot, (byte)value);
        }

        /// <summary>Peer: the host's state for one slot. Drives the same door and trapeze animation locally.</summary>
        internal void RemoteState(int slot, byte value)
        {
            if (carrier != null && carrier.IsServer || slot < 0 || slot > 1 || value > 4) return;
            if (State[slot] == value) return;
            State[slot] = value;
            if (visuals[slot] != null) visuals[slot].gameObject.SetActive(value != 2 && value != 3);
        }

        /// <summary>Host: start releasing the next stowed fighter (front first). False when none is left.</summary>
        internal bool Drop(int slot = -1)
        {
            if (carrier == null || !carrier.IsServer || !CanDrop) return false;
            if (slot < 0) slot = LaunchReady(0) ? 0 : 1;
            if (slot > 1 || !LaunchReady(slot)) return false;
            launchDeadline = Time.time + LaunchWait;
            SetSlot(slot, 1);
            return true;
        }

        /// <summary>Host: call every released, still-flying fighter back; returns how many were called.</summary>
        internal int Recall()
        {
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                if (!CanRecall(i) || !Released[i].gameObject.AddComponent<MegaplaneCapture>().Begin(this, i, false)) continue;
                SetSlot(i, 3);
                n++;
            }
            return n;
        }

        internal int WaveOff()
        {
            if (carrier == null || !carrier.IsServer || carrier.disabled) return 0;
            int n = 0;
            for (int i = 0; i < State.Length; i++)
                if (State[i] == 3 && Released[i] != null && Released[i].TryGetComponent(out MegaplaneCapture capture) &&
                    capture.WaveOff()) n++;
            return n;
        }

        private bool LaunchLaneClear(int slot)
        {
            Aircraft fighter = Released[1 - slot];
            return fighter == null || fighter.disabled || !fighter.TryGetComponent(out MegaplaneCapture departure) ||
                !departure.Departing || departure.BelowLaunchLane;
        }

        /// <summary>Where the caught fighter's root sits: the hidden stowed copy rides the yoke.</summary>
        internal Vector3 HookPoint(int slot) => visuals[slot].position;

        // The first fighter at its own gate gets the final approach. The other holds outboard.
        internal bool CanEnter(int slot)
        {
            if (!DeckReady || Door < DoorOpen - .5f || Arm[slot] < 89.5f) return false;
            if (arrivingSlot < 0) arrivingSlot = slot;
            return arrivingSlot == slot;
        }

        internal void ClearApproach(int slot)
        {
            if (arrivingSlot == slot) arrivingSlot = -1;
        }

        internal void CancelCapture(int slot)
        {
            if (carrier == null || !carrier.IsServer || State[slot] != 3) return;
            ClearApproach(slot);
            SetSlot(slot, 2);
        }

        internal bool Latch(int slot, Aircraft fighter)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (carrier == null || !carrier.IsServer || fighter == null || Released[slot] != fighter ||
                State[slot] != 3 || !CanEnter(slot) || spawner == null || !spawner.IsServer) return false;
            SetSlot(slot, 4);
            Released[slot] = null;
            spawner.ServerObjectManager.Destroy(fighter.gameObject);
            return true;
        }

        private void Update()
        {
            if (carrier == null) return;
            float dt = Time.deltaTime;
            SampleDeckRotation(dt);
            deckSteadyTime = UnsafeDeckReason == null ? Mathf.Min(DeckHoldTime, deckSteadyTime + dt) : 0f;
            KeyboardShortcut key = MegaplaneSettings.Current != null ? MegaplaneSettings.Current.DropFighterKey.Value : KeyboardShortcut.Empty;
            bool mine = GameManager.GetLocalAircraft(out Aircraft me) && me == carrier;
            if (mine && key.MainKey != KeyCode.None && key.IsDown())
                MegaplaneBayControls.Request(carrier, CarrierBayAction.Launch);
            KeyboardShortcut recall = MegaplaneSettings.Current != null ? MegaplaneSettings.Current.RecallFighterKey.Value : KeyboardShortcut.Empty;
            if (mine && recall.MainKey != KeyCode.None && recall.IsDown())
                MegaplaneBayControls.Request(carrier, CarrierBayAction.Recall);
            if (carrier.IsServer)
                for (int i = 0; i < State.Length; i++)
                {
                    if (State[i] == 3 && (Released[i] == null || Released[i].disabled || !HasMount(i))) CancelCapture(i);
                    if (State[i] == 1 && (carrier.disabled || !HasMount(i) || Time.time > launchDeadline)) SetSlot(i, 0);
                }
            bool busy = State[0] == 1 || State[1] == 1 || State[0] >= 3 || State[1] >= 3 || Arm[0] > 0.5f || Arm[1] > 0.5f;
            Door = Mathf.MoveTowards(Door, busy ? DoorOpen : 0f, DoorRate * dt);
            foreach ((Transform t, Quaternion rest, float sign) in doors) t.localRotation = rest * Quaternion.AngleAxis(sign * Door, Vector3.forward);
            for (int i = 0; i < 2; i++)
            {
                if (arms[i] == null || yokes[i] == null) continue;
                float target = (State[i] == 1 || State[i] == 3) && Door >= DoorOpen - 0.5f ? 90f : 0f;
                Arm[i] = Mathf.MoveTowards(Arm[i], target, ArmRate * dt);
                Quaternion swing = Quaternion.AngleAxis(-Arm[i], Vector3.right);
                arms[i].localRotation = swing;
                yokes[i].localPosition = arms[i].localPosition + swing * (Vector3.back * armL[i]);
                if (State[i] == 1 && Arm[i] >= 90f && carrier.IsServer && DeckReady && LaunchLaneClear(i)) Release(i);
                if (State[i] == 4 && Arm[i] <= 0f && carrier.IsServer)
                {
                    ReadyAt[i] = Time.time + RearmSeconds;
                    ClearApproach(i);
                    SetSlot(i, 0);
                }
            }
        }

        private void Release(int i)
        {
            Transform v = visuals[i];
            Vector3 position = v.position;
            Quaternion rotation = v.rotation;
            Vector3 up = carrier.transform.up;
            if (UsePredictedDeck)
            {
                position = DeckPosition + DeckRotation * transform.InverseTransformPoint(position);
                rotation = DeckRotation * Quaternion.Inverse(transform.rotation) * rotation;
                up = DeckRotation * Vector3.up;
            }
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer ||
                !(Encyclopedia.Lookup.TryGetValue(FighterKey, out UnitDefinition unit) && unit is AircraftDefinition def))
            {
                SetSlot(i, 0);
                return;
            }
            Loadout loadout = null;
            float fuel = 1f;
            foreach (StandardLoadout option in def.aircraftParameters?.StandardLoadouts ?? new StandardLoadout[0])
                if (option != null && !option.disabled && option.loadout != null) { loadout = option.loadout; fuel = option.FuelRatio > 0f ? option.FuelRatio : 1f; break; }
            Aircraft fighter = spawner.SpawnAircraft(null, def.unitPrefab, loadout, fuel, default, position.ToGlobalPosition(), rotation,
                DeckVelocity(position) - up * 7f, null, carrier.NetworkHQ, null, 0.8f, 0.8f);
            if (fighter == null)
            {
                SetSlot(i, 0);
                return;
            }
            Released[i] = fighter;
            SetSlot(i, 2);
            fighter.gameObject.AddComponent<MegaplaneCapture>().Begin(this, i, true);
        }
    }

    /// <summary>Host datalink control during launch and recovery. The fighter stays a live aircraft until
    /// its position, speed and attitude settle on the hook.</summary>
    [DefaultExecutionOrder(200)]
    internal sealed class MegaplaneCapture : MonoBehaviour
    {
        private const int Marshal = 0, Lineup = 1, Rise = 2, Settle = 3, Clear = 4;
        private const float GateBack = 340f, GateBelow = 82f, Under = 29f;
        private const float MaxRecoveryTime = 175f;
        private MegaplaneBay bay;
        private int slot;
        private Aircraft self;
        private Pilot pilot;
        private DatalinkPilot datalink;
        private PilotBaseState previous;
        private bool guiding, wasComplex, launch, departed, wavedOff, latched, finished, hasDriveSample;
        private Vector3 launchPoint, relativeVelocity, lastDriveVelocity, velocityDisturbance;
        private float started, phaseStarted, settled;
        internal int Phase { get; private set; }
        internal bool Departing => launch && !finished;
        internal bool BelowLaunchLane => departed;

        private void Awake() => self = GetComponent<Aircraft>();
        private void OnDisable() => RestorePilot();

        internal static bool FlyingCapture(Aircraft fighter) =>
            fighter.TryGetComponent(out MegaplaneCapture capture) && capture.guiding;

        internal static bool CanGuide(Aircraft fighter) => fighter != null && fighter.rb != null && !fighter.rb.isKinematic &&
            fighter.IsServer && fighter.LocalSim && !fighter.disabled && fighter.Player == null;

        internal bool Begin(MegaplaneBay owner, int index, bool departing)
        {
            if (!CanGuide(self))
            {
                Destroy(this);
                return false;
            }
            bay = owner;
            slot = index;
            launch = departing;
            started = phaseStarted = Time.time;
            launchPoint = bay.transform.InverseTransformPoint(bay.HookPoint(slot));
            return true;
        }

        internal bool WaveOff()
        {
            if (launch || finished || wavedOff || latched || bay == null || bay.Carrier == null ||
                bay.Carrier.rb == null || !CanGuide(self)) return false;
            wavedOff = true;
            Vector3 local = bay.DeckLocal(self.rb.position);
            if (Phase == Marshal && (local.sqrMagnitude > 280f * 280f || local.y < -70f)) Cancel();
            else ChangePhase(Clear);
            return true;
        }

        internal void Cancel()
        {
            if (finished) return;
            finished = true;
            RestorePilot();
            if (!launch && !latched && bay != null) bay.CancelCapture(slot);
            Destroy(this);
        }

        private void FixedUpdate()
        {
            if (finished || latched) return;
            if (bay == null || bay.Carrier == null || bay.Carrier.disabled || bay.Carrier.rb == null || !CanGuide(self) ||
                !bay.HasMount(slot) || bay.Released[slot] != self ||
                self.NetworkHQ != bay.Carrier.NetworkHQ || (!launch && bay.State[slot] != 3) ||
                (guiding && (pilot == null || pilot.dead || pilot.currentState != datalink)))
            {
                Cancel();
                return;
            }

            Rigidbody body = self.rb;
            Transform deck = bay.transform;
            if (!guiding && !TakePilot(body))
            {
                if (Time.time - started > 1f) Cancel();
                return;
            }

            Quaternion deckRotation = bay.DeckRotation;
            Vector3 actual = bay.DeckLocal(body.position);
            float relativeSpeedSquared = (body.velocity - bay.DeckVelocity(body.position)).sqrMagnitude;
            Vector3 hook = deck.InverseTransformPoint(bay.HookPoint(slot));
            if (launch)
            {
                Depart(body, actual);
                return;
            }

            float elapsed = Time.time - started;
            if (self.radarAlt < 95f || (Phase == Marshal && (elapsed > MaxRecoveryTime || actual.sqrMagnitude > 15000f * 15000f)))
            {
                Cancel();
                return;
            }
            if (Phase >= Lineup && Phase <= Settle &&
                (!bay.DeckReady || Time.time - phaseStarted > 45f ||
                    (Phase >= Rise && new Vector2(actual.x - hook.x, actual.z - hook.z).sqrMagnitude > 7f * 7f)))
                ChangePhase(Clear);

            Vector3 gate = new Vector3(slot == 0 ? -84f : 84f, hook.y - GateBelow, -GateBack);
            switch (Phase)
            {
                case Marshal:
                    Vector3 aim = gate;
                    // An aircraft leaving the near side of the carrier clears the wing before turning inboard.
                    if (actual.z > -145f && actual.z < 145f && Mathf.Abs(actual.x) < 180f && actual.y > -60f)
                        aim = new Vector3(Mathf.Sign(gate.x) * 190f, actual.y > 22f ? Mathf.Max(actual.y, 55f) : -76f, actual.z);
                    Guide(body, aim - actual, .12f, 105f, 15f, false);
                    if ((gate - actual).sqrMagnitude < 15f * 15f && relativeSpeedSquared < 12f * 12f &&
                        Quaternion.Angle(body.rotation, deckRotation) < 14f && bay.CanEnter(slot))
                        ChangePhase(Lineup);
                    break;
                case Lineup:
                    Vector3 under = hook - Vector3.up * Under - actual;
                    Guide(body, under, .32f, 23f, 8f, true);
                    if (under.sqrMagnitude < 2.5f * 2.5f && relativeSpeedSquared < 4f * 4f &&
                        Quaternion.Angle(body.rotation, deckRotation) < 7f)
                        ChangePhase(Rise);
                    break;
                case Rise:
                    Vector3 rise = hook - Vector3.up * 1.3f - actual;
                    Guide(body, rise, .65f, 3.8f, 3.2f, true);
                    if (rise.sqrMagnitude < 1.4f * 1.4f && relativeSpeedSquared < 2.6f * 2.6f)
                        ChangePhase(Settle);
                    break;
                case Settle:
                    Vector3 contact = hook - actual;
                    Guide(body, contact, 1.1f, 1.9f, 2.8f, true);
                    bool aligned = contact.sqrMagnitude <= 1.15f * 1.15f &&
                        relativeSpeedSquared < 1.5f * 1.5f && Quaternion.Angle(body.rotation, deckRotation) < 4f;
                    settled = aligned ? settled + Time.fixedDeltaTime : 0f;
                    if (settled >= .45f)
                    {
                        latched = true;
                        if (!bay.Latch(slot, self))
                        {
                            latched = false;
                            ChangePhase(Clear);
                        }
                    }
                    break;
                case Clear:
                    Vector3 clear = actual.y > hook.y - 27f
                        ? new Vector3(actual.x, hook.y - 38f, Mathf.Min(actual.z, hook.z - 10f))
                        : gate;
                    Guide(body, clear - actual, .32f, 29f, 8f, false);
                    if (actual.y < hook.y - 34f && actual.z < -205f)
                    {
                        bay.ClearApproach(slot);
                        if (wavedOff || elapsed > MaxRecoveryTime) Cancel();
                        else ChangePhase(Marshal);
                    }
                    break;
            }
        }

        private void Depart(Rigidbody body, Vector3 actual)
        {
            // Drop clear of the neighboring trapeze first, then fly forward and outboard.
            departed |= actual.y <= launchPoint.y - 22f;
            float outboard = slot == 0 ? -60f : 60f;
            Vector3 aim = launchPoint + (departed ? new Vector3(outboard, -42f, 195f) : new Vector3(0f, -30f, 12f));
            Guide(body, aim - actual, .45f, departed ? 39f : 12f, 9f, false);
            if (departed && (actual.z > launchPoint.z + 150f || Time.time - started > 16f)) Cancel();
        }

        private void Guide(Rigidbody body, Vector3 error,
            float gain, float speed, float acceleration, bool alignWithDeck)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 desired = Vector3.ClampMagnitude(error * gain, speed);
            if (!alignWithDeck)
            {
                desired.z = Mathf.Max(desired.z, -66f);
                desired.y = Mathf.Clamp(desired.y, -27f, 25f);
            }
            Vector3 command = Vector3.MoveTowards(relativeVelocity, desired, acceleration * dt);
            Quaternion heading = bay.DeckRotation;
            Vector3 worldVelocity = bay.DeckVelocity(body.position) + heading * command;
            // Aero and engine forces still run between our fixed steps. Compensate their measured velocity
            // change so the fighter can hold close alignment without moving the body to a prescribed position.
            if (hasDriveSample)
                velocityDisturbance = Vector3.Lerp(velocityDisturbance,
                    Vector3.ClampMagnitude(body.velocity - lastDriveVelocity, 4f), Mathf.Clamp01(dt * 12f));
            hasDriveSample = true;
            lastDriveVelocity = body.velocity = worldVelocity - velocityDisturbance;
            body.angularVelocity = Vector3.zero;
            if (!alignWithDeck && worldVelocity.sqrMagnitude > 1f)
            {
                float bank = Mathf.Clamp(-(command.x - relativeVelocity.x) * 1.7f / dt, -22f, 22f);
                heading = Quaternion.LookRotation(worldVelocity, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
            }
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, heading, (alignWithDeck ? 13f : 26f) * dt));
            relativeVelocity = command;
        }

        private void ChangePhase(int next)
        {
            Phase = next;
            phaseStarted = Time.time;
            settled = 0f;
        }

        private bool TakePilot(Rigidbody body)
        {
            pilot = self.pilots?.FirstOrDefault(p => p != null && !p.dead && p.currentState != null);
            if (pilot == null) return false;
            previous = pilot.currentState;
            datalink = new DatalinkPilot(launch);
            pilot.SwitchState(datalink);
            if (pilot.currentState != datalink) return false;
            guiding = true;
            wasComplex = !self.simplePhysics;
            if (wasComplex) self.SetSimplePhysics();
            relativeVelocity = Quaternion.Inverse(bay.DeckRotation) * (body.velocity - bay.DeckVelocity(body.position));
            return true;
        }

        private void RestorePilot()
        {
            if (!guiding) return;
            guiding = false;
            if (latched || !CanGuide(self)) return;
            if (wasComplex && self.simplePhysics) self.SetComplexPhysics();
            if (pilot != null && !pilot.dead && pilot.currentState == datalink) pilot.SwitchState(previous);
        }

        private sealed class DatalinkPilot : PilotBaseState
        {
            public DatalinkPilot(bool launch) => stateDisplayName = launch ? "Groza departure" : "Groza recovery";

            public override void EnterState(Pilot owner)
            {
                Initialize(owner);
                if (aircraft.gearState != LandingGear.GearState.LockedRetracted) aircraft.SetGear(false);
            }

            public override void UpdateState(Pilot owner) { }
            public override void LeaveState() { }

            public override void FixedUpdateState(Pilot owner)
            {
                controlInputs.pitch = controlInputs.roll = controlInputs.yaw = controlInputs.brake = 0f;
                controlInputs.throttle = .55f;
                aircraft.FilterInputs();
            }
        }
    }
}

