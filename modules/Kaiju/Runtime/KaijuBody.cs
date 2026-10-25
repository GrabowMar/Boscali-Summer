using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Kaiju.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Kaiju.Runtime
{
    /// <summary>
    /// The kaiju as every peer sees it: the skinned, animated body under <c>Datum.origin</c> at 15x, solid capsule colliders
    /// on layer Statics (bullets, missiles and aircraft hit it), the hardpoint units ridden on their bones, glowing
    /// blisters, the head turning to its aim point, stomps and the beam. The host's <see cref="KaijuBrain"/> writes the
    /// public state directly; clients get it from <see cref="KaijuNet"/> and ease towards it. Cosmetic apart from the
    /// colliders and the hardpoint poses, which every peer computes the same way.
    /// </summary>
    [DefaultExecutionOrder(200)] // after the Animator and the floating-origin shift in CameraStateManager.LateUpdate
    internal sealed class KaijuBody : MonoBehaviour
    {
        public const string NodePrefix = "KAIJU#";
        private static readonly Color Hot = new Color(1f, 0.34f, 0.06f);
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly string[] AimChain = { "Neck0_M", "Neck1_M", "Neck2_M", "Neck3_M", "Neck4_M", "Neck5_M", "Head_M" };
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static readonly HashSet<uint> NodeIds = new HashSet<uint>();
        private static readonly string[] Torso = { "Root_M", "Chest_M", "Neck0_M", "Neck3_M", "Head_M", "Jaw_M" }; // hull segment starts

        public byte Gen;
        public GlobalPosition Position;
        public float Yaw;
        public KaijuAction Action = KaijuAction.Rise;
        public float ActionStart;
        public GlobalPosition Aim;
        public bool HasAim;
        public float WalkPlayback = 1f;
        public bool Smooth;
        public Action<Vector3> OnStomp;

        public Building[] Nodes { get; private set; }
        public int Broken { get; private set; }
        /// <summary>Sounds started since spawn (automation readout).</summary>
        public int Sounds { get; private set; }
        /// <summary>Worst vertical gap (m) between a fully planted foot and the ground under it after the ground IK (automation readout).</summary>
        public float FootGap { get; private set; }

        private Transform model;
        private Animator animator;
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly HashSet<Collider> own = new HashSet<Collider>();
        private readonly List<GameObject> torso = new List<GameObject>();
        private Vector3[] offsets;
        private Renderer[] blisters;
        private bool[] popped, attached;
        private MaterialPropertyBlock block;
        private GlobalPosition shownPos;
        private Vector3 shownAim; // global; clients ease it between the 10 Hz states so the beam and head sweep instead of stepping
        private float shownYaw, shownStart = -1f, aimWeight, nextScan, nextBeamFx, walkPhase = -1f;
        private KaijuAction shownAction = (KaijuAction)255;
        private readonly float[] planted = new float[KaijuRig.Limbs.Length]; // ground-IK weight per leg, 0..1
        private readonly Vector3[] footGround = new Vector3[KaijuRig.Limbs.Length]; // where each foot last landed
        private LineRenderer core, glow;
        private Material coreMat, glowMat;
        private Light mouthLight, beamLight;
        private AudioSource voice, limb, ground;

        public static KaijuBody Create(byte gen, GlobalPosition at, float yaw)
        {
            GameObject prefab = KaijuAssets.Body;
            if (prefab == null || Datum.origin == null) return null;
            spotScale = SpotScaleSetting?.Invoke() ?? 1f;
            GameObject go = Instantiate(prefab, Datum.origin, false);
            go.name = "KAIJU";
            go.transform.localScale = Vector3.one * KaijuRules.Scale;
            go.transform.SetPositionAndRotation(at.ToLocalPosition(), Quaternion.Euler(0f, yaw, 0f));
            var body = go.AddComponent<KaijuBody>();
            body.Init(gen, at, yaw);
            return body;
        }

        public Transform Bone(string name) => bones.TryGetValue(name, out Transform t) ? t : null;
        public Vector3 Chest => bones["Chest_M"].position;
        public Vector3 Mouth => Vector3.Lerp(bones["HeadEnd_M"].position, bones["JawEnd_M"].position, 0.45f);
        public Vector3 SpotWorld(int i) => bones[KaijuRig.Spots[i].Bone].TransformPoint(offsets[i]);
        /// <summary>Hitbox radius in metres: the blister's size plus 20 m, so a missile that bursts on the hide right beside a
        /// weak spot still counts (tests/ingame/kaiju-tune.json: eye and knee shots burst 7-26 m short on the hull).</summary>
        public static float HitboxRadius(int i) => KaijuRig.Spots[i].Radius * KaijuRules.Scale * spotScale + 20f;
        /// <summary>[Kaiju] WeakSpotScale, set by the director; read once per spawn so blister and hitbox always match.</summary>
        public static Func<float> SpotScaleSetting;
        private static float spotScale = 1f;
        public bool Owns(Collider c) => c != null && own.Contains(c);
        public bool Alive(int i) => Nodes[i] != null && !Nodes[i].disabled;
        /// <summary>Spawned and since destroyed; the game deletes a dead building a moment later, so null counts.</summary>
        public bool Dead(int i) => attached[i] && (Nodes[i] == null || Nodes[i].disabled);

        /// <summary>The kaiju never hurts its own weak spots (spike blasts on its back, its own footfalls).</summary>
        public static bool IsSelfHit(Unit target, PersistentID dealer) =>
            NodeIds.Count > 0 && target != null && NodeIds.Contains(dealer.Id) && NodeIds.Contains(target.persistentID.Id);
        public bool IsKaiju(Unit unit) => unit != null && Array.IndexOf(Nodes, unit) >= 0;

        private void Init(byte gen, GlobalPosition at, float yaw)
        {
            Gen = gen;
            Position = shownPos = at;
            Yaw = shownYaw = yaw;
            ActionStart = Time.time;
            model = transform.Find("Model");
            animator = model.GetComponent<Animator>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            // The Animator has not run yet: this is the bind pose the weak spots were measured on.
            KaijuRig.Spot[] spots = KaijuRig.Spots;
            offsets = new Vector3[spots.Length];
            blisters = new Renderer[spots.Length];
            popped = new bool[spots.Length];
            attached = new bool[spots.Length];
            Nodes = new Building[spots.Length];
            block = new MaterialPropertyBlock();
            for (int i = 0; i < spots.Length; i++)
            {
                Transform bone = bones[spots[i].Bone];
                Vector3 world = model.TransformPoint(spots[i].Bind);
                offsets[i] = bone.InverseTransformPoint(world);
                if (spots[i].Kind == Weakspot.Eye || KaijuAssets.Blister == null) continue;
                Transform blister = Instantiate(KaijuAssets.Blister, bone, false).transform;
                blister.position = world;
                blister.rotation = Quaternion.LookRotation(world - bone.position);
                blister.localScale = Vector3.one * (spots[i].Radius * 2f * KaijuRules.Scale * spotScale / bone.lossyScale.x);
                blisters[i] = blister.GetComponentInChildren<Renderer>();
            }

            foreach ((string from, string to, float radius) in KaijuRig.Segments)
            {
                if (!bones.TryGetValue(from, out Transform a) || !bones.TryGetValue(to, out Transform b)) continue;
                var hull = new GameObject("Hull_" + from) { layer = PhysicsLayers.Statics };
                hull.transform.SetParent(a, false);
                hull.transform.position = (a.position + b.position) * 0.5f;
                hull.transform.rotation = Quaternion.FromToRotation(Vector3.up, b.position - a.position);
                hull.transform.localScale = Vector3.one / a.lossyScale.x; // world-unit capsule under the 0.01-scaled rig
                var capsule = hull.AddComponent<CapsuleCollider>();
                float r = radius * KaijuRules.Scale;
                capsule.radius = r;
                capsule.height = Vector3.Distance(a.position, b.position) + 2f * r;
                capsule.direction = 1;
                own.Add(capsule);
                if (Array.IndexOf(Torso, from) >= 0) torso.Add(hull);
            }
            // The hulls are coarser than the skin, so most weak spots measured on it sit inside one, where nothing can touch
            // them: missiles burst on the hull and the game's blast traces stop there (tests/ingame/kaiju-tune.json: throat,
            // eyes, knees took 0 from five AGMs). Lift each hitbox centre just out of every hull; the blister stays on the skin.
            var lifted = new List<string>();
            for (int i = 0; i < spots.Length; i++)
            {
                Transform bone = bones[spots[i].Bone];
                Vector3 skin = bone.TransformPoint(offsets[i]), p = skin;
                for (int pass = 0; pass < 3; pass++)
                    foreach ((string from, string to, float radius) in KaijuRig.Segments)
                    {
                        if (!bones.TryGetValue(from, out Transform a) || !bones.TryGetValue(to, out Transform b)) continue;
                        Vector3 ab = b.position - a.position;
                        Vector3 axis = a.position + ab * Mathf.Clamp01(Vector3.Dot(p - a.position, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                        float clear = radius * KaijuRules.Scale + 5f;
                        if ((p - axis).sqrMagnitude < clear * clear) p = axis + (p - axis).normalized * clear;
                    }
                offsets[i] = bone.InverseTransformPoint(p);
                if (p != skin) lifted.Add(spots[i].Name + " " + Vector3.Distance(p, skin).ToString("0") + " m");
            }
            if (lifted.Count > 0) Debug.Log("[Kaiju] hitboxes lifted out of the hull: " + string.Join(", ", lifted));
            var rb = gameObject.AddComponent<Rigidbody>(); // kinematic compound: moving bone colliders stay cheap
            rb.isKinematic = true;
            rb.useGravity = false;

            mouthLight = NewLight("MouthLight", bones["Head_M"], 1200f);
            beamLight = NewLight("BeamLight", transform, 2500f);
            core = NewBeam("BeamCore", new Color(6f, 4f, 2f), out coreMat);
            glow = NewBeam("BeamGlow", new Color(2.6f, 0.75f, 0.12f), out glowMat);
            voice = NewSound("KaijuVoice", bones["Head_M"], 2500f);
            limb = NewSound("KaijuLimb", bones["Chest_M"], 1200f);
            ground = NewSound("KaijuGround", transform, 2000f);
        }

        /// <summary>Host: hand over a hardpoint it just spawned. Clients find theirs by unique name.</summary>
        public void Attach(int i, Building node)
        {
            Nodes[i] = node;
            attached[i] = true;
            NodeIds.Add(node.persistentID.Id);
            foreach (Collider c in node.GetComponentsInChildren<Collider>(true))
            {
                if (c is SphereCollider sphere) sphere.radius = HitboxRadius(i);
                own.Add(c);
            }
            node.transform.position = SpotWorld(i);
            if (i == KaijuRig.HeartIndex)
                foreach (GameObject hull in torso) hull.AddComponent<KaijuHeartHide>().Heart = node.GetComponentInChildren<UnitPart>();
        }

        /// <summary>A spike is an IR missile wearing the kaiju's horn: hide the missile, keep its smoke and seeker.</summary>
        public static void Restyle(Missile missile)
        {
            if (missile == null || KaijuAssets.Spike == null || missile.transform.Find("KaijuSpike") != null) return;
            foreach (MeshRenderer r in missile.GetComponentsInChildren<MeshRenderer>(true)) r.forceRenderingOff = true;
            GameObject spike = Instantiate(KaijuAssets.Spike, missile.transform, false);
            spike.name = "KaijuSpike";
            spike.transform.localScale = Vector3.one * (60f / Mathf.Max(0.001f, missile.transform.lossyScale.x));
        }

        /// <summary>Where a beam from <paramref name="from"/> stops: the first thing that is not the kaiju.</summary>
        public Vector3 BeamEnd(Vector3 from, Vector3 dir, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(from, dir, Hits, KaijuRules.LaserRange, ~0, QueryTriggerInteraction.Ignore);
            float best = KaijuRules.LaserRange;
            for (int i = 0; i < n; i++)
            {
                if (Owns(Hits[i].collider) || Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                hit = Hits[i];
            }
            return from + dir * best;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (Smooth && shownStart >= 0f)
            {
                float k = 1f - Mathf.Exp(-dt * 3f);
                shownPos = new GlobalPosition(Vector3.Lerp(shownPos.AsVector3(), Position.AsVector3(), k));
                shownYaw = Mathf.LerpAngle(shownYaw, Yaw, k);
                shownAim = Vector3.Lerp(shownAim, Aim.AsVector3(), 1f - Mathf.Exp(-dt * 12f));
            }
            else
            {
                shownPos = Position;
                shownYaw = Yaw;
                shownAim = Aim.AsVector3();
            }
            transform.SetPositionAndRotation(shownPos.ToLocalPosition(), Quaternion.Euler(0f, shownYaw, 0f));

            if (Action != shownAction || !Mathf.Approximately(ActionStart, shownStart))
            {
                string state = KaijuRig.State(Action);
                if (Action == shownAction) animator.Play(state, 0, 0f); // same attack again: restart it
                else animator.CrossFadeInFixedTime(state, Action == KaijuAction.Death ? 0.3f : 0.6f);
                if (Action == KaijuAction.Death || Action == KaijuAction.Roar) Shake(Chest, 30000f, 1.6f);
                Sound(Action);
                shownAction = Action;
                shownStart = ActionStart;
            }
            animator.speed = KaijuRig.Playback(Action) * (Action == KaijuAction.Walk || Action == KaijuAction.Rise ? WalkPlayback : 1f);

            GroundLimbs(dt);
            AimHead(dt);
            PoseNodes();
            Footfalls();
            Effects(dt);
        }

        /// <summary>
        /// Plant each foot on whatever is under it (terrain, plaza, runway). The clip is authored off the body's origin, which
        /// is only the ground under its centre, and the toes sit ~20 m above that origin, so unadjusted feet float on flat
        /// ground and sink on slopes. Each leg's lowest contact is probed down to the nearest surface that is not the kaiju,
        /// and the leg is bent to put that contact on it. A swinging foot (Walk) and a raised arm (any attack) keep the clip; a
        /// dying kaiju lets go.
        /// </summary>
        private void GroundLimbs(float dt)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            bool walking = info.IsName("Walk") && !animator.IsInTransition(0);
            float phase = Mathf.Repeat(info.normalizedTime, 1f), gap = 0f;
            for (int i = 0; i < KaijuRig.Limbs.Length; i++)
            {
                KaijuRig.Limb limb = KaijuRig.Limbs[i];
                Vector3 contact = LowestOf(limb.Contact);
                bool hit = Surface(contact, out float ground);
                footGround[i] = hit ? new Vector3(contact.x, ground, contact.z) : contact;
                bool standing = Action == KaijuAction.Walk || Action == KaijuAction.Rise || Action == KaijuAction.Idle;
                bool bearing = !limb.Fore || standing; // the arms only carry weight when walking or standing
                bool want = hit && bearing && Action != KaijuAction.Death && (!walking || limb.Planted(phase));
                planted[i] = Mathf.MoveTowards(planted[i], want ? 1f : 0f, dt * 6f);
                if (planted[i] <= 0f) continue;

                Transform end = bones[limb.End];
                Quaternion endRotation = end.rotation;
                float lift = hit ? ground - contact.y : 0f; // no surface: fade out where the clip has it
                Solve(bones[limb.Root], bones[limb.Mid], end, end.position + Vector3.up * lift, planted[i]);
                end.rotation = endRotation; // the foot keeps its clip orientation; only the legs above it bend
                if (hit && planted[i] >= 1f) gap = Mathf.Max(gap, Mathf.Abs(LowestOf(limb.Contact).y - ground));
            }
            FootGap = gap;
        }

        /// <summary>Highest surface below a point that is not one of the kaiju's own colliders.</summary>
        private bool Surface(Vector3 from, out float y)
        {
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 300f, Vector3.down, Hits, 600f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            y = float.MinValue;
            for (int i = 0; i < n; i++)
                if (!Owns(Hits[i].collider) && Hits[i].point.y > y) y = Hits[i].point.y;
            return y > float.MinValue;
        }

        private Vector3 LowestOf(string[] names)
        {
            Vector3 low = bones[names[0]].position;
            for (int i = 1; i < names.Length; i++)
            {
                Vector3 p = bones[names[i]].position;
                if (p.y < low.y) low = p;
            }
            return low;
        }

        /// <summary>
        /// Two-bone IK: swing the root so the mid joint sits on the same side of the root-to-target line as the clip's knee,
        /// then the mid so the end reaches the target. <paramref name="weight"/> blends the rotations towards the clip.
        /// </summary>
        private static void Solve(Transform root, Transform mid, Transform end, Vector3 target, float weight)
        {
            Vector3 a = root.position, b = mid.position;
            float l1 = Vector3.Distance(a, b), l2 = Vector3.Distance(b, end.position);
            Vector3 toward = target - a;
            float reach = Mathf.Clamp(toward.magnitude, 0.01f, (l1 + l2) * 0.999f);
            Vector3 dir = toward.normalized, side = Vector3.Cross(dir, b - a);
            if (side.sqrMagnitude < 1e-8f) return;
            float bend = Mathf.Acos(Mathf.Clamp((l1 * l1 + reach * reach - l2 * l2) / (2f * l1 * reach), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 knee = a + Quaternion.AngleAxis(bend, side.normalized) * dir * l1;
            root.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(b - a, knee - a), weight) * root.rotation;
            Vector3 m = mid.position;
            mid.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(end.position - m, target - m), weight) * mid.rotation;
        }

        /// <summary>Turn the neck and head towards <see cref="Aim"/> on top of the clip, spread over the chain.</summary>
        private void AimHead(float dt)
        {
            bool beam = Action == KaijuAction.LaserCharge || Action == KaijuAction.LaserFire;
            bool look = Action == KaijuAction.Walk || Action == KaijuAction.Idle || Action == KaijuAction.Spike || Action == KaijuAction.Roar;
            float want = HasAim && (beam || look) ? (beam ? 1f : 0.6f) : 0f;
            aimWeight = Mathf.MoveTowards(aimWeight, want, dt * 0.8f);
            if (aimWeight <= 0.001f) return;

            Transform head = bones["Head_M"];
            Vector3 facing = transform.InverseTransformDirection(bones["HeadEnd_M"].position - head.position);
            Vector3 wanted = transform.InverseTransformDirection(new GlobalPosition(shownAim).ToLocalPosition() - Mouth);
            float dy = Mathf.DeltaAngle(Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, Mathf.Atan2(wanted.x, wanted.z) * Mathf.Rad2Deg);
            float dp = Mathf.Atan2(wanted.y, new Vector2(wanted.x, wanted.z).magnitude) * Mathf.Rad2Deg -
                       Mathf.Atan2(facing.y, new Vector2(facing.x, facing.z).magnitude) * Mathf.Rad2Deg;
            dy = Mathf.Clamp(dy, -70f, 70f) * aimWeight / AimChain.Length;
            dp = Mathf.Clamp(dp, -45f, 60f) * aimWeight / AimChain.Length;
            foreach (string name in AimChain)
            {
                Transform t = bones[name];
                t.rotation = Quaternion.AngleAxis(dy, transform.up) * Quaternion.AngleAxis(-dp, transform.right) * t.rotation;
            }
        }

        private void PoseNodes()
        {
            if (Time.time >= nextScan && Array.IndexOf(Nodes, null) >= 0)
            {
                nextScan = Time.time + 1f;
                ScanNodes();
            }
            int broken = 0;
            for (int i = 0; i < Nodes.Length; i++)
            {
                Building node = Nodes[i];
                if (node != null && !node.disabled) node.transform.SetPositionAndRotation(SpotWorld(i), transform.rotation);
                else if (Dead(i) && !popped[i]) Pop(i);
                if (popped[i] && i != KaijuRig.HeartIndex) broken++;
            }
            Broken = broken;
        }

        private void ScanNodes()
        {
            string prefix = NodePrefix + Gen + ":";
            List<Unit> units = UnitRegistry.allUnits;
            for (int u = 0; u < units.Count; u++)
            {
                if (!(units[u] is Building b) || b.UniqueName == null || !b.UniqueName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (int.TryParse(b.UniqueName.Substring(prefix.Length), out int i) && i >= 0 && i < Nodes.Length && Nodes[i] != b) Attach(i, b);
            }
        }

        private void Pop(int i)
        {
            popped[i] = true;
            Vector3 at = SpotWorld(i);
            if (blisters[i] != null) blisters[i].enabled = false;
            if (Nodes[i] != null)
                foreach (Collider c in Nodes[i].GetComponentsInChildren<Collider>(true)) c.enabled = false;
            if (i == KaijuRig.HeartIndex)
                foreach (GameObject hull in torso) Destroy(hull.GetComponent<KaijuHeartHide>());
            ParticleFx.Spawn("ops-rod-impact", at, Quaternion.LookRotation(at - Chest), 1.6f);
            ParticleFx.Spawn("fx-fuel-fireball", at, Quaternion.identity, 4f);
            Shake(at, 15000f, 1f);
        }

        /// <summary>Walk-cycle footfalls at each foot's landing ground: dust, a ground shake, and (host) <see cref="OnStomp"/> crush damage.</summary>
        private void Footfalls()
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName("Walk") || animator.IsInTransition(0))
            {
                walkPhase = -1f;
                return;
            }
            float phase = Mathf.Repeat(info.normalizedTime, 1f);
            if (walkPhase >= 0f)
                for (int i = 0; i < KaijuRig.Limbs.Length; i++)
                {
                    float at = KaijuRig.Limbs[i].Landing;
                    if (!(walkPhase < phase ? walkPhase < at && at <= phase : at > walkPhase || at <= phase)) continue;
                    Vector3 foot = footGround[i];
                    ParticleFx.Spawn("fx-collapse-dust", foot, Quaternion.identity, 8f);
                    Shake(foot, 20000f, 0.8f);
                    OnStomp?.Invoke(foot);
                    Step(foot);
                }
            walkPhase = phase;
        }

        private void Effects(float dt)
        {
            float t = Time.time - ActionStart;
            float mouth = Action == KaijuAction.LaserCharge ? Mathf.Clamp01(t / 3f) * 30f
                : Action == KaijuAction.LaserFire ? 45f + 10f * Mathf.Sin(Time.time * 37f)
                : Action == KaijuAction.Roar ? 6f : 0f;
            mouthLight.transform.position = Mouth;
            mouthLight.intensity = Mathf.MoveTowards(mouthLight.intensity, mouth, dt * 60f);
            mouthLight.enabled = mouthLight.intensity > 0.01f;

            bool firing = Action == KaijuAction.LaserFire && HasAim;
            core.enabled = glow.enabled = beamLight.enabled = firing;
            if (firing)
            {
                Vector3 from = Mouth;
                Vector3 dir = (new GlobalPosition(shownAim).ToLocalPosition() - from).normalized;
                Vector3 end = BeamEnd(from, dir, out RaycastHit hit);
                float flicker = 1f + 0.15f * Mathf.Sin(Time.time * 53f) + 0.1f * Mathf.Sin(Time.time * 17f);
                core.SetPosition(0, from);
                core.SetPosition(1, end);
                core.startWidth = 40f * flicker;
                core.endWidth = 60f * flicker;
                glow.SetPosition(0, from);
                glow.SetPosition(1, end);
                glow.startWidth = 120f * flicker;
                glow.endWidth = 220f * flicker;
                beamLight.transform.position = end - dir * 60f;
                beamLight.intensity = 60f * flicker;
                if (hit.collider != null && Time.time >= nextBeamFx)
                {
                    nextBeamFx = Time.time + 0.18f;
                    ParticleFx.Spawn("fx-fuel-fireball", hit.point, Quaternion.identity, 3f);
                    Shake(hit.point, 6000f, 0.5f);
                }
            }

            bool heartOpen = Broken >= KaijuRules.HeartGuard;
            for (int i = 0; i < blisters.Length; i++)
            {
                if (blisters[i] == null || popped[i]) continue;
                bool heart = i == KaijuRig.HeartIndex;
                float pulse = 3.5f + 2.5f * Mathf.Sin(Time.time * (heart && heartOpen ? 6f : 2.4f) + i * 1.7f);
                if (heart) pulse = heartOpen ? pulse * 3f : 1.2f;
                block.SetColor(EmissionId, Hot * pulse);
                blisters[i].SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// The action's sound: the voice from the head, a spike volley from the back, a claw from its wrist. Clips over 3.5 s
        /// are built squeezed (PICKS speed in tools/Build-SoundClips.py) and play back at pitch 1/speed.
        /// </summary>
        private void Sound(KaijuAction action)
        {
            switch (action)
            {
                case KaijuAction.Roar: Play(voice, "kaiju-roar", 1f / 1.45f); break;
                case KaijuAction.LaserCharge: Play(voice, "kaiju-charge", 1f); break;
                case KaijuAction.LaserFire: Play(voice, "kaiju-beam", 1f / 1.45f); break;
                case KaijuAction.Flinch: Play(voice, "kaiju-shriek", UnityEngine.Random.Range(0.92f, 1.06f)); break;
                case KaijuAction.Death:
                    Play(voice, "kaiju-bellow", 1f / 1.6f);
                    Play(ground, "building-collapse", 0.5f, 4.8f); // the chest hits the ground about 5 s in
                    break;
                case KaijuAction.Spike:
                    limb.transform.SetParent(bones["Chest_M"], false);
                    Play(limb, "kaiju-spike", 1f, KaijuRig.SpikeRelease / KaijuRig.Playback(action));
                    break;
                case KaijuAction.SwipeL:
                case KaijuAction.SwipeR:
                    limb.transform.SetParent(bones[action == KaijuAction.SwipeL ? "Wrist_L" : "Wrist_R"], false);
                    Play(limb, "kaiju-swipe", 1f, KaijuRig.SwipeHitStart / KaijuRig.Playback(action) - 0.5f); // the rush peaks 0.8 s in
                    break;
                default:
                    voice.Stop(); // walking again: a beam cut short goes quiet with it
                    break;
            }
        }

        private void Play(AudioSource source, string clipName, float pitch, float delay = 0f)
        {
            source.Stop();
            AudioClip clip = SfxClips.Get(clipName);
            if (clip == null || !FxVoiceBus.TryOneShot(delay + clip.length / pitch)) return;
            source.clip = clip;
            source.pitch = pitch;
            source.PlayDelayed(delay);
            Sounds++;
        }

        private void Step(Vector3 foot)
        {
            AudioClip clip = SfxClips.Get("kaiju-step");
            if (clip == null || !FxVoiceBus.TryOneShot(clip.length)) return;
            ground.transform.position = foot;
            ground.pitch = UnityEngine.Random.Range(0.85f, 1.1f); // one clip, never the same boom twice
            ground.PlayOneShot(clip);
            Sounds++;
        }

        /// <summary>
        /// A 3D source for a 900 m creature: full volume within <paramref name="near"/> metres, then about 6 dB quieter per
        /// doubling of distance; past 40 km it holds that floor, so the kaiju is heard across the theatre.
        /// </summary>
        private static AudioSource NewSound(string name, Transform parent, float near)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f; // floating-origin shifts would read as speed
            source.priority = 0;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = near;
            source.maxDistance = 40000f;
            if (SoundManager.i != null) source.outputAudioMixerGroup = SoundManager.i.EffectsMixer;
            return source;
        }

        private Light NewLight(string name, Transform parent, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.15f);
            light.range = range;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        private LineRenderer NewBeam(string name, Color color, out Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.numCapVertices = 6;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader);
            MakeAdditive(material);
            material.SetColor(BaseColorId, color);
            material.color = color;
            line.sharedMaterial = material;
            line.enabled = false;
            return line;
        }

        private void OnDestroy()
        {
            NodeIds.Clear(); // one kaiju per peer
            if (coreMat != null) Destroy(coreMat);
            if (glowMat != null) Destroy(glowMat);
        }

        // Local copies of Support's CineFx.Shake and Vanguard's LanceFxPlayer.MakeAdditive: a module may not import a sibling.
        private static void Shake(Vector3 world, float reach, float strength)
        {
            var csm = SceneSingleton<CameraStateManager>.i;
            if (csm == null) return;
            Camera c = csm.mainCamera != null ? csm.mainCamera : Camera.main;
            float k = Mathf.Clamp01(1f - (c != null ? Vector3.Distance(c.transform.position, world) : 2000f) / reach);
            if (k > 0.02f) csm.ShakeCamera(Mathf.Lerp(0.2f, 3.4f, k * k) * strength, Mathf.Lerp(0.3f, 4.2f, k) * strength);
        }

        private static void MakeAdditive(Material m)
        {
            if (!m.shader.name.StartsWith("Universal Render Pipeline", StringComparison.Ordinal)) return;
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    /// <summary>
    /// Once the heart is open its torso, neck and head hulls take hits for it ("HIT THE CHEST"): the heart sits under the
    /// jutting head, where a diving missile cannot reach its own hitbox (tests/ingame/kaiju-tune.json). On every peer, so a
    /// client's gun claims its hits against the heart too.
    /// </summary>
    internal sealed class KaijuHeartHide : MonoBehaviour, IDamageable
    {
        public UnitPart Heart;
        private static int lastFrame;
        private static uint lastDealer;

        /// <summary>One hit per shooter per frame: a round's pierce trace or a blast runs through several overlapping hulls.</summary>
        public void TakeDamage(float pierce, float blast, float affected, float fire, float impact, PersistentID dealer)
        {
            if (Time.frameCount == lastFrame && dealer.Id == lastDealer) return;
            lastFrame = Time.frameCount;
            lastDealer = dealer.Id;
            Heart.TakeDamage(pierce, blast, affected, fire, impact, dealer);
        }

        public void ApplyDamage(float pierce, float blast, float fire, float impact) => Heart.ApplyDamage(pierce, blast, fire, impact);
        public void TakeShockwave(Vector3 origin, float overpressure, float blastPower) { }
        public void Detach(Vector3 velocity, Vector3 relativePos) { }
        public ArmorProperties GetArmorProperties() => Heart.GetArmorProperties();
        public Unit GetUnit() => Heart.GetUnit();
        public float GetMass() => Heart.GetMass();
        public Transform GetTransform() => Heart.GetTransform();
    }
}
