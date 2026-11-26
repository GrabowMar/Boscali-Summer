using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
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

        /// <summary>Render-only copy of the donor's visible airframe, hung with its top-centre on the hook point.</summary>
        private static void Visual(GameObject prefab, Transform slot, string key)
        {
            var groups = prefab.GetComponentsInChildren<LODGroup>(true).Select(g => (Source: g, Levels: g.GetLODs()))
                .Where(g => g.Levels.Length > 0).ToArray();
            var top = new HashSet<Renderer>(groups.SelectMany(g => g.Levels[0].renderers));
            var lower = new HashSet<Renderer>(groups.SelectMany(g => g.Levels.Skip(1).SelectMany(l => l.renderers)));
            var root = new GameObject("BayFighter_" + key).transform;
            root.SetParent(slot, false);
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            var bounds = new Bounds();
            bool any = false;
            var meshes = new Dictionary<Renderer, Mesh>();
            var copies = new Dictionary<Renderer, MeshRenderer>();
            foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (!r.enabled || mf == null || mf.sharedMesh == null || !Shown(r.transform, prefab.transform)) continue;
                meshes.Add(r, mf.sharedMesh);
                if (lower.Contains(r) && !top.Contains(r)) continue;
                Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                copies.Add(r, CopyRenderer(r, mf.sharedMesh, root, m));
                Bounds b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) * 2 - 1, (c & 2) - 1, (c & 4) / 2 - 1)));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            root.localPosition = -new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            CopyLods(prefab.transform, root, toRoot, groups, meshes, copies);
        }

        private static MeshRenderer CopyRenderer(Renderer source, Mesh mesh, Transform parent, Matrix4x4 pose)
        {
            var go = new GameObject(source.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pose.GetColumn(3);
            go.transform.localRotation = pose.rotation;
            go.transform.localScale = pose.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            return renderer;
        }

        private static void CopyLods(Transform prefab, Transform visual, Matrix4x4 toRoot,
            (LODGroup Source, LOD[] Levels)[] groups, Dictionary<Renderer, Mesh> meshes, Dictionary<Renderer, MeshRenderer> copies)
        {
            // A renderer can repeat within one group. Two separate groups cannot govern the same copy.
            var shared = new HashSet<Renderer>(groups.SelectMany(g => g.Levels.SelectMany(l => l.renderers).Distinct())
                .GroupBy(r => r).Where(g => g.Count() > 1).Select(g => g.Key));
            foreach (var (source, levels) in groups)
            {
                if (!source.enabled || levels.Length < 2 || !Shown(source.transform, prefab)) continue;
                Renderer[][] keep = levels.Select(l => l.renderers
                    .Where(r => r != null && r is MeshRenderer && r.enabled && Shown(r.transform, prefab)).Distinct().ToArray()).ToArray();
                if (keep.Any(level => level.Length == 0 || level.Any(r => !meshes.ContainsKey(r) || shared.Contains(r)))) continue;

                Matrix4x4 frame = toRoot * source.transform.localToWorldMatrix;
                float size = source.size * Mathf.Max(frame.GetColumn(0).magnitude,
                    Mathf.Max(frame.GetColumn(1).magnitude, frame.GetColumn(2).magnitude));
                if (!(size > 0f) || float.IsInfinity(size)) continue;

                var holder = new GameObject("BayFighterLOD_" + source.name).transform;
                holder.SetParent(visual, false);
                var lods = new LOD[levels.Length];
                for (int i = 0; i < levels.Length; i++)
                {
                    var renderers = new Renderer[keep[i].Length];
                    for (int j = 0; j < renderers.Length; j++)
                    {
                        Renderer r = keep[i][j];
                        if (copies.TryGetValue(r, out MeshRenderer copy)) copy.transform.SetParent(holder, false);
                        else copies.Add(r, copy = CopyRenderer(r, meshes[r], holder, toRoot * r.transform.localToWorldMatrix));
                        renderers[j] = copy;
                    }
                    lods[i] = new LOD(levels[i].screenRelativeTransitionHeight, renderers) { fadeTransitionWidth = levels[i].fadeTransitionWidth };
                }
                var group = holder.gameObject.AddComponent<LODGroup>();
                group.fadeMode = source.fadeMode;
                group.animateCrossFading = source.animateCrossFading;
                group.SetLODs(lods);
                group.localReferencePoint = frame.MultiplyPoint3x4(source.localReferencePoint);
                group.size = size;
            }
        }
    }

    internal sealed class MegaplaneBay : MonoBehaviour
    {
        [SerializeField] internal string FighterKey;
        private const float DoorOpen = 100f;   // degrees: the door leaves swing down and outboard
        private const float DoorRate = 35f, ArmRate = 22f;   // deg/s: the doors lead, the arms follow
        private Aircraft carrier;
        private int arrivingSlot = -1;
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

        private void OnDestroy() => MegaplaneNet.Unregister(this);

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
            bool Ready(int i) => State[i] == 0 && Time.time >= ReadyAt[i];
            if (slot < 0) slot = Ready(0) ? 0 : Ready(1) ? 1 : -1;
            if (slot < 0 || slot > 1 || !Ready(slot) || !carrier.IsServer) return false;
            SetSlot(slot, 1);
            return true;
        }

        /// <summary>Host: call every released, still-flying fighter back; returns how many were called.</summary>
        internal int Recall()
        {
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                Aircraft f = Released[i];
                if (State[i] != 2 || f == null || f.disabled || !carrier.IsServer) continue;
                SetSlot(i, 3);
                MegaplaneCapture c = f.GetComponent<MegaplaneCapture>() ?? f.gameObject.AddComponent<MegaplaneCapture>();
                c.Bay = this;
                c.Slot = i;
                n++;
            }
            return n;
        }

        /// <summary>Where the caught fighter's root sits: the hidden stowed copy rides the yoke.</summary>
        internal Vector3 HookPoint(int slot) => visuals[slot].position;

        // The first fighter at its own gate gets the final approach. The other holds outboard.
        internal bool CanEnter(int slot)
        {
            if (Arm[slot] < 89.5f) return false;
            if (arrivingSlot < 0 || State[arrivingSlot] != 3) arrivingSlot = slot;
            return arrivingSlot == slot;
        }

        internal void CancelCapture(int slot)
        {
            if (carrier != null && carrier.IsServer && State[slot] == 3) SetSlot(slot, 2);
        }

        internal void Latch(int slot, Aircraft fighter)
        {
            if (State[slot] != 3) return;
            SetSlot(slot, 4);
            Released[slot] = null;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner != null && spawner.IsServer && fighter != null) spawner.ServerObjectManager.Destroy(fighter.gameObject);   // stowed, not lost
        }

        private void Update()
        {
            KeyboardShortcut key = MegaplaneSettings.Current != null ? MegaplaneSettings.Current.DropFighterKey.Value : KeyboardShortcut.Empty;
            // the pilot's key works on any peer: the host runs the bay directly, a client asks it over the wire
            bool mine = GameManager.GetLocalAircraft(out Aircraft me) && me == carrier;
            if (mine && key.MainKey != KeyCode.None && key.IsDown())
            {
                if (carrier.IsServer) Drop();
                else MegaplaneNet.Ask(carrier, MegaplaneWire.DropVerb);
            }
            KeyboardShortcut recall = MegaplaneSettings.Current != null ? MegaplaneSettings.Current.RecallFighterKey.Value : KeyboardShortcut.Empty;
            if (mine && recall.MainKey != KeyCode.None && recall.IsDown())
            {
                if (carrier.IsServer) Recall();
                else MegaplaneNet.Ask(carrier, MegaplaneWire.RecallVerb);
            }
            for (int i = 0; i < 2; i++)   // host: a fighter shot down on the way back frees its slot
                if (carrier.IsServer && State[i] == 3 && (Released[i] == null || Released[i].disabled)) SetSlot(i, 2);
            float dt = Time.deltaTime;
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
                if (State[i] == 1 && Arm[i] >= 90f && carrier.IsServer) Release(i);
                if (State[i] == 4 && Arm[i] <= 0f && carrier.IsServer) { SetSlot(i, 0); ReadyAt[i] = Time.time + RearmSeconds; }
            }
        }

        private void Release(int i)
        {
            SetSlot(i, 2);
            Transform v = visuals[i];
            if (v == null) return;
            Vector3 position = v.position;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !(Encyclopedia.Lookup.TryGetValue(FighterKey, out UnitDefinition unit) && unit is AircraftDefinition def)) return;
            Loadout loadout = null;
            float fuel = 1f;
            foreach (StandardLoadout option in def.aircraftParameters?.StandardLoadouts ?? new StandardLoadout[0])
                if (option != null && !option.disabled && option.loadout != null) { loadout = option.loadout; fuel = option.FuelRatio > 0f ? option.FuelRatio : 1f; break; }
            Released[i] = spawner.SpawnAircraft(null, def.unitPrefab, loadout, fuel, default, position.ToGlobalPosition(), carrier.transform.rotation,
                carrier.rb.velocity - carrier.transform.up * 4f, null, carrier.NetworkHQ, null, 0.8f, 0.8f);
        }
    }

    /// <summary>Datalink capture of one recalled bay fighter (host). The carrier flies it, as automated aerial refuelling
    /// and DARPA Gremlins recovery do: to a gate behind and below the bay at up to 90 m/s over the carrier's speed, to a
    /// point 25 m straight under the lowered hook, then up at walking pace onto the hook, where the bay latches it.</summary>
    [DefaultExecutionOrder(200)]
    internal sealed class MegaplaneCapture : MonoBehaviour
    {
        internal MegaplaneBay Bay;
        internal int Slot;
        internal int Phase;   // 0 to the gate, 1 under the hook, 2 rising onto it
        private Aircraft self;
        private bool guiding, mergedForCapture;
        private Vector3 localPosition, localVelocity;
        private const float GateBack = 260f, GateBelow = 70f, Under = 25f;

        private void Awake() => self = GetComponent<Aircraft>();

        internal static bool FlyingCapture(Aircraft fighter) =>
            fighter.TryGetComponent(out MegaplaneCapture capture) && capture.guiding;

        private void FixedUpdate()
        {
            if (Bay == null || Bay.Carrier == null || Bay.Carrier.disabled || self == null || self.disabled ||
                self.Player != null || !self.IsServer || !self.LocalSim || Bay.State[Slot] != 3)
            {
                bool stowed = Bay != null && Bay.State[Slot] == 4;
                if (Bay != null) Bay.CancelCapture(Slot);
                guiding = false;
                if (mergedForCapture && !stowed && self != null && !self.disabled && self.LocalSim && self.simplePhysics)
                    self.SetComplexPhysics();
                Destroy(this);
                return;
            }
            Rigidbody rb = self.rb, carrier = Bay.Carrier.rb;
            Transform c = Bay.transform;
            if (!guiding)
            {
                // A close-range fighter has a chain of independently simulated aero bodies.
                // Merge only its still-attached parts with the native LOD API for the approach.
                guiding = true;
                mergedForCapture = !self.simplePhysics;
                if (mergedForCapture) self.SetSimplePhysics();
                localPosition = c.InverseTransformPoint(rb.position);
                localVelocity = c.InverseTransformDirection(rb.velocity - carrier.GetPointVelocity(rb.position));
            }
            // Keep the datalink path in deck coordinates. Accumulating a world-space position
            // would put a recalled fighter at the old datum when the game shifts the map origin.
            Vector3 hook = c.InverseTransformPoint(Bay.HookPoint(Slot));
            Vector3 aim = Phase == 0 ? hook + new Vector3(Slot == 0 ? -42f : 42f, -GateBelow, -GateBack)
                : Phase == 1 ? hook - Vector3.up * Under : hook;
            Vector3 err = aim - localPosition;
            float gain = Phase == 0 ? .12f : Phase == 1 ? .35f : .8f;
            float speed = Phase == 0 ? 90f : Phase == 1 ? 20f : 3.5f;
            float dt = Time.fixedDeltaTime;
            localVelocity = Vector3.MoveTowards(localVelocity, Vector3.ClampMagnitude(err * gain, speed), 26f * dt);
            localPosition += localVelocity * dt;
            Vector3 next = c.TransformPoint(localPosition);
            Vector3 deckVelocity = carrier.GetPointVelocity(next);
            Vector3 velocity = deckVelocity + c.TransformDirection(localVelocity);
            rb.MovePosition(next + deckVelocity * dt);
            rb.velocity = velocity;
            rb.angularVelocity = Vector3.zero;
            Quaternion look = Phase == 0 && velocity.sqrMagnitude > 1f ? Quaternion.LookRotation(velocity, c.up) : c.rotation;
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, look, (Phase == 0 ? 25f : 10f) * dt));
            if (Phase == 0 && err.magnitude < 12f && Bay.CanEnter(Slot)) Phase = 1;
            else if (Phase == 1 && err.magnitude < 2f) Phase = 2;
            else if (Phase == 2 && err.magnitude < 1.5f && localVelocity.magnitude < 4f &&
                Vector3.Distance(c.InverseTransformPoint(rb.position), hook) < 4f) Bay.Latch(Slot, self);
        }
    }
}

