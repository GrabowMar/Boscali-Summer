using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    // An owned visual skeleton, with a dedicated cockpit mesh and the full native reflection mesh.
    internal sealed class CockpitPilotRig
    {
        private const int MaxNodes = 64;
        internal const float GripReachAllowance = .08f;
        internal const float GripShoulderAllowance = .05f;
        internal const float GripRestAllowance = .20f;
        private readonly Transform[] sources = new Transform[MaxNodes];
        private readonly Transform[] copies = new Transform[MaxNodes];
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private static readonly int HeadCenterId = Shader.PropertyToID("_HeadCenter");
        private static readonly int HeadMaskId = Shader.PropertyToID("_HeadWorldToMask");
        private static readonly int HeadModeId = Shader.PropertyToID("_HeadMaskMode");
        private static readonly int BodyVisibleId = Shader.PropertyToID("_BodyVisible");
        private static readonly int LightId = Shader.PropertyToID("_LightLevel");
        private static readonly string[] NativeBoneNames = { "pelvis", "chest", "neck", "head", "upperarm_L", "forearm_L", "hand_L",
            "upperarm_R", "forearm_R", "hand_R", "thigh_R", "shin_R", "foot_R", "thigh_L", "shin_L", "foot_L" };
        private Animator nativeAnimator;
        private SkinnedMeshRenderer nativeRenderer;
        private AnimatorCullingMode originalCulling;
        private GameObject root;
        private Material material;
        private Material bodyMaterial;
        private SkinnedMeshRenderer firstPerson;
        private Camera camera;
        private Transform frame, neck, headEnd, rightUpper, rightLower, rightHand, leftUpper, leftLower, leftHand;
        private Transform leftFoot, rightFoot, stick, throttle;
        private Vector3 stickGrip, throttleGrip;
        private Quaternion stickRotation, throttleRotation;
        private int count;
        private float phase, lean, pitchLean, headPitch, headYaw, pedals;
        private float rightArmLength, leftArmLength, stickRestFit, throttleRestFit;
        private bool bodyVisible, hooked, stickOnRight = true;
        internal SkinnedMeshRenderer Renderer { get; private set; }
        internal SkinnedMeshRenderer BodyRenderer => firstPerson != null ? firstPerson : Renderer;
        internal Transform Head { get; private set; }
        internal Transform Chest { get; private set; }
        internal int TransformCount => count;
        internal Material Material => material;
        internal Transform Frame => frame;
        internal bool StickOnRight => stickOnRight;
        internal Transform StickHand => stickOnRight ? rightHand : leftHand;
        internal Transform ThrottleHand => stickOnRight ? leftHand : rightHand;
        internal Transform StickControl => stick;
        internal Transform ThrottleControl => throttle;
        internal Vector3 StickAnchor => stickGrip;
        internal Vector3 ThrottleAnchor => throttleGrip;
        internal float StickRestFit => stickRestFit;
        internal float ThrottleRestFit => throttleRestFit;
        internal bool StickBound => stick != null && StickHand != null && (stickOnRight ? rightUpper != null && rightLower != null : leftUpper != null && leftLower != null);
        internal bool ThrottleBound => throttle != null && ThrottleHand != null && (stickOnRight ? leftUpper != null && leftLower != null : rightUpper != null && rightLower != null);
        internal float StickGripError => StickBound ? Vector3.Distance(StickHand.position, stick.TransformPoint(stickGrip)) : -1f;
        internal float ThrottleGripError => ThrottleBound ? Vector3.Distance(ThrottleHand.position, throttle.TransformPoint(throttleGrip)) : -1f;
        internal bool Valid
        {
            get
            {
                if (root == null || Renderer == null || nativeRenderer == null ||
                    nativeRenderer.sharedMesh == null || nativeRenderer.sharedMesh != Renderer.sharedMesh || Head == null) return false;
                if (bodyMaterial != null && (firstPerson == null || firstPerson.sharedMesh == null)) return false;
                for (int i = 0; i < count; i++) if (sources[i] == null || copies[i] == null) return false;
                return true;
            }
        }

        internal bool Bind(SkinnedMeshRenderer source, Animator animator, Shader shader, Camera renderCamera)
        {
            Release();
            if (source == null || source.sharedMesh == null || shader == null || !shader.isSupported || renderCamera == null) return false;
            Transform[] bones = source.bones;
            if (bones == null || bones.Length != 16) return false;
            Transform common = source.transform;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) return false;
                while (common != null && bones[i] != common && !bones[i].IsChildOf(common)) common = common.parent;
                if (common == null) return false;
            }
            root = new GameObject("Boscali.CockpitPilot");
            root.SetActive(false);
            root.transform.SetParent(common.parent, false);
            if (!CopyTree(common, root.transform)) { Release(); return false; }
            Head = Bone("head"); Chest = Bone("chest"); neck = Bone("neck"); headEnd = Bone("head_end");
            rightUpper = Bone("upperarm_R"); rightLower = Bone("forearm_R"); rightHand = Bone("hand_R");
            leftUpper = Bone("upperarm_L"); leftLower = Bone("forearm_L"); leftHand = Bone("hand_L");
            leftFoot = Bone("foot_L"); rightFoot = Bone("foot_R");
            if (Head == null || Chest == null || neck == null) { Release(); return false; }
            Transform rendererNode = Map(source.transform);
            if (rendererNode == null) { Release(); return false; }
            Renderer = rendererNode.gameObject.AddComponent<SkinnedMeshRenderer>();
            Renderer.sharedMesh = source.sharedMesh;
            var copiedBones = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++) copiedBones[i] = Map(bones[i]);
            Renderer.bones = copiedBones;
            Renderer.rootBone = Map(source.rootBone);
            Renderer.localBounds = source.localBounds;
            Renderer.updateWhenOffscreen = true;
            Renderer.shadowCastingMode = ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            material = new Material(shader) { name = "Boscali.PilotBody", hideFlags = HideFlags.HideAndDontSave };
            Material native = source.sharedMaterial;
            BorrowMaterial(native, material);
            Renderer.sharedMaterial = material;
            Mesh closeMesh = PilotShaderBundle.GetFirstPersonMesh();
            if (headEnd != null && Bone("helmetCamPoint") != null && CompatibleMesh(source.sharedMesh, closeMesh, bones))
            {
                var node = new GameObject("FirstPersonBody");
                node.layer = 3;
                node.transform.SetParent(rendererNode, false);
                firstPerson = node.AddComponent<SkinnedMeshRenderer>();
                firstPerson.sharedMesh = closeMesh;
                firstPerson.bones = copiedBones;
                firstPerson.rootBone = Renderer.rootBone;
                firstPerson.localBounds = source.localBounds;
                firstPerson.updateWhenOffscreen = true;
                firstPerson.quality = SkinQuality.Bone4;
                firstPerson.shadowCastingMode = ShadowCastingMode.Off;
                firstPerson.receiveShadows = false;
                firstPerson.lightProbeUsage = LightProbeUsage.Off;
                firstPerson.reflectionProbeUsage = ReflectionProbeUsage.Off;
                bodyMaterial = new Material(shader) { name = "Boscali.FirstPersonBody", hideFlags = HideFlags.HideAndDontSave };
                BorrowMaterial(native, bodyMaterial);
                bodyMaterial.SetFloat(HeadModeId, 0);
                firstPerson.sharedMaterial = bodyMaterial;
            }
            nativeRenderer = source;
            camera = renderCamera;
            frame = common.parent != null ? common.parent : common;
            nativeAnimator = animator;
            if (nativeAnimator != null)
            {
                originalCulling = nativeAnimator.cullingMode;
                nativeAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Camera.onPreCull += OnPreCull;
            hooked = true;
            SetBodyVisible(true);
            root.SetActive(true);
            CopySeatedPose();
            rightArmLength = ArmLength(rightUpper, rightLower, rightHand);
            leftArmLength = ArmLength(leftUpper, leftLower, leftHand);
            UpdateHeadMask();
            return true;
        }

        private static bool CompatibleMesh(Mesh source, Mesh replacement, Transform[] bones)
        {
            if (source == null || replacement == null || source.name != "pilot" || source.vertexCount != 3814 ||
                replacement.subMeshCount != 1 || bones.Length != NativeBoneNames.Length) return false;
            for (int i = 0; i < bones.Length; i++) if (bones[i].name != NativeBoneNames[i]) return false;
            // Skinning bind matrices remain CPU-accessible even when native vertex buffers are unreadable.
            // Compare once at bind, never read back or bake native geometry in the update loop.
            Matrix4x4[] original = source.bindposes, derived = replacement.bindposes;
            if (original.Length != 16 || derived.Length != 16) return false;
            for (int i = 0; i < 16; i++)
                for (int element = 0; element < 16; element++)
                    if (Mathf.Abs(original[i][element] - derived[i][element]) > .00001f) return false;
            return true;
        }

        private static void BorrowMaterial(Material native, Material owned)
        {
            if (native == null) return;
            string baseMap = native.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            if (native.HasProperty(baseMap))
            {
                Texture texture = native.GetTexture(baseMap);
                if (texture != null) owned.SetTexture("_BaseMap", texture);
                owned.SetTextureScale("_BaseMap", native.GetTextureScale(baseMap));
                owned.SetTextureOffset("_BaseMap", native.GetTextureOffset(baseMap));
            }
            if (native.HasProperty("_BaseColor")) owned.SetColor("_BaseColor", native.GetColor("_BaseColor"));
            BorrowMap(native, owned, "_BumpMap", "_NORMALMAP", "_HasNormalMap");
            BorrowMap(native, owned, "_MetallicGlossMap", "_METALLICSPECGLOSSMAP", "_HasMetallicMap");
            BorrowMap(native, owned, "_OcclusionMap", "_OCCLUSIONMAP", "_HasOcclusionMap");
            BorrowFloat(native, owned, "_BumpScale");
            BorrowFloat(native, owned, "_Metallic");
            BorrowFloat(native, owned, "_Smoothness");
            BorrowFloat(native, owned, "_OcclusionStrength");
            BorrowFloat(native, owned, "_SmoothnessTextureChannel");
            BorrowFloat(native, owned, "_SpecularHighlights");
        }

        private static void BorrowMap(Material native, Material owned, string property, string keyword, string flag)
        {
            Texture texture = native.HasProperty(property) ? native.GetTexture(property) : null;
            bool enabled = texture != null && native.IsKeywordEnabled(keyword);
            owned.SetFloat(flag, enabled ? 1 : 0);
            if (!enabled) return;
            owned.SetTexture(property, texture);
            owned.SetTextureScale(property, native.GetTextureScale(property));
            owned.SetTextureOffset(property, native.GetTextureOffset(property));
        }

        private static void BorrowFloat(Material native, Material owned, string property)
        {
            if (native.HasProperty(property)) owned.SetFloat(property, native.GetFloat(property));
        }

        private bool CopyTree(Transform source, Transform parent)
        {
            if (count >= MaxNodes) return false;
            var node = new GameObject(source.name);
            node.layer = 3; // Verified PhysicsLayers.Cockpit; native pilot remains on Default.
            Transform copy = node.transform;
            copy.SetParent(parent, false);
            sources[count] = source; copies[count++] = copy;
            copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
            for (int i = 0; i < source.childCount; i++) if (!CopyTree(source.GetChild(i), copy)) return false;
            return true;
        }

        private Transform Map(Transform source)
        {
            for (int i = 0; i < count; i++) if (sources[i] == source) return copies[i];
            return null;
        }

        internal Transform Bone(string name)
        {
            for (int i = 0; i < count; i++) if (copies[i] != null && copies[i].name == name) return copies[i];
            return null;
        }

        internal void SetFrame(Transform value) { if (value != null) frame = value; }

        internal void SetControlHandedness(bool rightHandStick)
        {
            if (stickOnRight == rightHandStick) return;
            stickOnRight = rightHandStick;
            stick = throttle = null;
            stickRestFit = throttleRestFit = 0f;
            CopySeatedPose();
        }

        internal void FitControlReach(bool joystick, float maximumDistance)
        {
            float nativeLength = joystick == stickOnRight ? rightArmLength : leftArmLength;
            if (!PilotPoseMath.Finite(maximumDistance) || nativeLength <= .001f) return;
            // Fit the owned sleeve once to the authored full throw. Preserve glove size,
            // seat/head position and the existing small per-frame reach allowances.
            float fit = Mathf.Clamp(maximumDistance + .015f - nativeLength - GripReachAllowance - GripShoulderAllowance, 0f, GripRestAllowance);
            if (joystick) stickRestFit = fit; else throttleRestFit = fit;
        }

        private static float ArmLength(Transform upper, Transform lower, Transform hand) => upper != null && lower != null && hand != null
            ? Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position) : 0f;

        internal static Vector3 GripPoint(Mesh mesh)
        {
            Bounds bounds = mesh.bounds;
            // Verified native curved cyclic heads; whole-shaft X/Z bounds put the
            // palm in empty space ahead of the physical hand grip.
            if (mesh.name == "utilityHelo1_joystick" && mesh.vertexCount == 1467 && Mathf.Abs(bounds.max.y - .8688719f) < .001f)
                return new Vector3(-.011583f, .768872f, -.138844f);
            if (mesh.name == "quadVTOL1_joystick" && mesh.vertexCount == 1436 && Mathf.Abs(bounds.max.y - .8252178f) < .001f)
                return new Vector3(-.015220f, .725218f, -.005443f);
            if (mesh.name == "sfb_joystick" && mesh.vertexCount == 1439 && Mathf.Abs(bounds.max.y - .8013563f) < .001f)
                return new Vector3(-.01625866f, .7013563f, -.1366216f);
            if (mesh.name == "EW1_joystick" && mesh.vertexCount == 1439 && Mathf.Abs(bounds.max.y - .7087711f) < .001f)
                return new Vector3(-.01666565f, .6087711f, -.1366215f);
            if (mesh.name == "fastBomber1_joystick" && mesh.vertexCount == 1485 && Mathf.Abs(bounds.max.y - .5689913f) < .001f)
                return new Vector3(-.01666571f, .4689913f, -.1312654f);
            if (mesh.name == "sfb_throttle" && mesh.vertexCount == 300 && Mathf.Abs(bounds.max.y - .2599013f) < .001f)
                return new Vector3(.00003283f, .2099013f, -.2090676f);
            if (mesh.name == "EW1_throttle" && mesh.vertexCount == 321 && Mathf.Abs(bounds.max.y - .1328866f) < .001f)
                return new Vector3(0, .08288665f, -.2308236f);
            if (mesh.name == "fastBomber1_throttle" && mesh.vertexCount == 440 && Mathf.Abs(bounds.max.y - .2641369f) < .001f)
                return new Vector3(0, .2141369f, -.2165338f);
            Vector3 point = bounds.center;
            point.y = Mathf.Lerp(bounds.center.y, bounds.max.y, .8f);
            return point;
        }

        internal static bool TryFindGrip(Transform lever, Transform hand, Transform shoulder, bool rightHand,
            out Vector3 localGrip, out Quaternion localRotation, out float wristDistance)
            => TryFindGrip(lever, hand, shoulder, rightHand, rightHand, out localGrip, out localRotation, out wristDistance);

        internal static bool TryFindGrip(Transform lever, Transform hand, Transform shoulder, bool rightHand, bool joystick,
            out Vector3 localGrip, out Quaternion localRotation, out float wristDistance)
        {
            localGrip = Vector3.zero;
            localRotation = Quaternion.identity;
            // The native lap pose is not a reach limit. Reject by shoulder/arm
            // geometry below, then use lap distance only to choose among controls.
            wristDistance = float.PositiveInfinity;
            if (lever == null || hand == null || hand.parent == null || shoulder == null) return false;
            float maxReach = Vector3.Distance(shoulder.position, hand.parent.position) + Vector3.Distance(hand.parent.position, hand.position);
            bool found = false;
            // Binding only: retain transforms/anchors afterwards, never scan meshes per frame.
            MeshFilter[] meshes = lever.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshes.Length && i < 16; i++)
            {
                MeshFilter filter = meshes[i];
                if (filter == null || filter.sharedMesh == null) continue;
                Vector3 point = GripPoint(filter.sharedMesh);
                // Native seated hands rest in the lap. Fit the curled glove to the handle
                // with its thumb up, rather than capturing that unrelated resting orientation.
                Transform handle = filter.transform;
                Vector3 palm = handle.TransformPoint(point);
                Quaternion rotation;
                if (joystick) rotation = Quaternion.LookRotation(rightHand ? -handle.right : handle.right, handle.forward);
                else
                {
                    // A collective is gripped along the reach direction, avoiding a right-angle wrist bend.
                    Vector3 fingers = (palm - shoulder.position).normalized;
                    Vector3 thumb = Vector3.ProjectOnPlane(handle.up, fingers);
                    if (thumb.sqrMagnitude < .000001f) thumb = Vector3.ProjectOnPlane(handle.right, fingers);
                    if (fingers.sqrMagnitude < .000001f || thumb.sqrMagnitude < .000001f) continue;
                    rotation = Quaternion.LookRotation(rightHand ? Vector3.Cross(fingers, thumb.normalized) : Vector3.Cross(thumb.normalized, fingers), fingers);
                }
                // Native glove centre in world metres; imported bones carry scale 100.
                Vector3 palmOffset = rotation * new Vector3(0, .085f, .02f);
                Vector3 world = palm - palmOffset;
                float distance = (world - hand.position).sqrMagnitude;
                if (distance >= wristDistance || Vector3.Distance(shoulder.position, world) > maxReach + GripRestAllowance + GripReachAllowance + GripShoulderAllowance) continue;
                localGrip = lever.InverseTransformPoint(world);
                localRotation = Quaternion.Inverse(lever.rotation) * rotation;
                wristDistance = distance; found = true;
            }
            return found;
        }

        internal void CopySeatedPose()
        {
            for (int i = 0; i < count; i++)
            {
                Transform source = sources[i], copy = copies[i];
                if (source == null || copy == null) continue;
                copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
            }
            FitArm(rightUpper, rightLower, rightHand, stickOnRight ? stickRestFit : throttleRestFit);
            FitArm(leftUpper, leftLower, leftHand, stickOnRight ? throttleRestFit : stickRestFit);
        }

        private static void FitArm(Transform upper, Transform lower, Transform hand, float extra)
        {
            if (extra <= 0f) return;
            float length = ArmLength(upper, lower, hand);
            if (length <= .001f) return;
            float scale = 1f + extra / length;
            lower.localPosition *= scale;
            hand.localPosition *= scale;
        }

        internal void SetControls(Transform stickTransform, Transform throttleTransform)
        {
            stick = stickTransform; throttle = throttleTransform;
            if (stick == null) stickRestFit = 0f;
            if (throttle == null) throttleRestFit = 0f;
            if (stick != null && StickHand != null)
            { stickGrip = stick.InverseTransformPoint(StickHand.position); stickRotation = Quaternion.Inverse(stick.rotation) * StickHand.rotation; }
            if (throttle != null && ThrottleHand != null)
            { throttleGrip = throttle.InverseTransformPoint(ThrottleHand.position); throttleRotation = Quaternion.Inverse(throttle.rotation) * ThrottleHand.rotation; }
        }

        internal void SetControls(Transform stickTransform, Vector3 stickLocalGrip, Transform throttleTransform, Vector3 throttleLocalGrip)
        {
            SetControls(stickTransform, throttleTransform);
            stickGrip = stickLocalGrip; throttleGrip = throttleLocalGrip;
        }

        internal void AttachControls(Transform stickTransform, Vector3 stickLocalGrip, Quaternion stickLocalRotation,
            Transform throttleTransform, Vector3 throttleLocalGrip, Quaternion throttleLocalRotation)
        {
            // A delayed second control must not recalibrate a hand already holding a tilted lever.
            if (!StickBound && stickTransform != null && StickHand != null)
            {
                stick = stickTransform; stickGrip = stickLocalGrip;
                stickRotation = stickLocalRotation;
            }
            if (!ThrottleBound && throttleTransform != null && ThrottleHand != null)
            {
                throttle = throttleTransform; throttleGrip = throttleLocalGrip;
                throttleRotation = throttleLocalRotation;
            }
        }

        internal void Pose(float pitch, float roll, float yaw, float throttleInput, Vector3 forceG,
            Quaternion lookLocal, float dt, bool motion, bool comfort)
        {
            if (!Valid || frame == null) return;
            float blend = PilotPoseMath.Smoothing(dt, 10f);
            // CopySeatedPose ran first: reapply the last presentation state even when time is paused.
            if (blend > 0f) phase = (phase + Mathf.Min(dt, .05f) * 1.55f) % (Mathf.PI * 2f);
            Vector3 look = lookLocal.eulerAngles;
            float wantedPitch = Mathf.Clamp(Mathf.DeltaAngle(0, look.x), -45f, 35f);
            float wantedYaw = Mathf.Clamp(Mathf.DeltaAngle(0, look.y), -75f, 75f);
            if (!PilotPoseMath.Finite(wantedPitch)) wantedPitch = 0f;
            if (!PilotPoseMath.Finite(wantedYaw)) wantedYaw = 0f;
            headPitch = Mathf.Lerp(headPitch, wantedPitch, blend);
            headYaw = Mathf.Lerp(headYaw, wantedYaw, blend);
            if (motion)
            {
                lean = Mathf.Lerp(lean, PilotPoseMath.TorsoDegrees(forceG.x, comfort), blend);
                float brace = PilotPoseMath.Finite(forceG.y) ? Mathf.Clamp((forceG.y - 1f) * .2f, -.6f, 1.2f) * (comfort ? .25f : 1f) : 0f;
                pitchLean = Mathf.Lerp(pitchLean, Mathf.Clamp(PilotPoseMath.TorsoDegrees(forceG.z, comfort) + brace,
                    comfort ? -.5f : -2f, comfort ? .5f : 2f), blend);
                Vector2 torso = Vector2.ClampMagnitude(new Vector2(pitchLean, lean), comfort ? .5f : 2f);
                Chest.rotation = FrameRotation(Quaternion.Euler(torso.x, 0, torso.y)) * Chest.rotation;
                float breath = PilotPoseMath.BreathMetres(phase, comfort);
                Chest.position += frame.up * breath;
                pedals = Mathf.Lerp(pedals, PilotPoseMath.Input(yaw), blend);
                if (leftFoot != null) leftFoot.rotation = FrameRotation(Quaternion.Euler(pedals * 8f, 0, 0)) * leftFoot.rotation;
                if (rightFoot != null) rightFoot.rotation = FrameRotation(Quaternion.Euler(-pedals * 8f, 0, 0)) * rightFoot.rotation;
            }
            else { lean = pitchLean = pedals = 0f; }
            // Contact follows the native lever after animation, including when optional body motion is off.
            if (StickBound) SolveArm(stickOnRight ? rightUpper : leftUpper, stickOnRight ? rightLower : leftLower,
                StickHand, stick.TransformPoint(stickGrip), stick.rotation * stickRotation);
            if (ThrottleBound) SolveArm(stickOnRight ? leftUpper : rightUpper, stickOnRight ? leftLower : rightLower,
                ThrottleHand, throttle.TransformPoint(throttleGrip), throttle.rotation * throttleRotation);
            neck.rotation = FrameRotation(Quaternion.Euler(headPitch * .15f, headYaw * .15f, 0)) * neck.rotation;
            Head.rotation = FrameRotation(Quaternion.Euler(headPitch * .85f, headYaw * .85f, 0)) * Head.rotation;
            UpdateHeadMask();
        }

        private Quaternion FrameRotation(Quaternion rotation) => frame.rotation * rotation * Quaternion.Inverse(frame.rotation);

        private static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Quaternion gripRotation)
        {
            if (upper == null || lower == null || hand == null) return;
            Vector3 origin = upper.position, elbow = lower.position, end = hand.position;
            float a = Vector3.Distance(origin, elbow), b = Vector3.Distance(elbow, end);
            Vector3 reach = target - origin;
            float distance = reach.magnitude;
            if (a <= .001f || b <= .001f || distance <= .001f || distance > a + b + GripReachAllowance + GripShoulderAllowance ||
                distance < Mathf.Abs(a - b) - GripReachAllowance || !PilotPoseMath.Finite(distance)) return;
            // A short reach of the owned shoulder covers side-stick corners without
            // moving the seat/head or increasing the existing arm-stretch allowance.
            // Native pose copying resets this displacement before every update.
            float shoulderReach = Mathf.Clamp(distance - (a + b + GripReachAllowance) + .001f, 0f, GripShoulderAllowance);
            if (shoulderReach > 0f)
            {
                upper.position += reach.normalized * shoulderReach;
                origin = upper.position; elbow = lower.position; end = hand.position;
                reach = target - origin; distance = reach.magnitude;
            }
            // Distribute any small full-throw fit across both arm segments rather than stretching only the wrist.
            float reachScale = Mathf.Max(1f, distance / (a + b));
            a *= reachScale; b *= reachScale;
            distance = Mathf.Clamp(distance, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 direction = reach.normalized;
            Vector3 pole = elbow - origin;
            pole -= direction * Vector3.Dot(pole, direction);
            if (pole.sqrMagnitude < .000001f) pole = Vector3.Cross(direction, upper.right);
            if (pole.sqrMagnitude < .000001f) return;
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 nextElbow = origin + direction * along + pole.normalized * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(elbow - origin, nextElbow - origin) * upper.rotation;
            lower.position = nextElbow;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
            // Turning the palm rolls the forearm; it should not twist the wrist joint.
            Vector3 axis = hand.localPosition.normalized;
            Quaternion wrist = Quaternion.Inverse(lower.rotation) * gripRotation * Quaternion.Inverse(hand.localRotation);
            float axial = Vector3.Dot(new Vector3(wrist.x, wrist.y, wrist.z), axis);
            float twist = Mathf.DeltaAngle(0, 2f * Mathf.Atan2(axial, wrist.w) * Mathf.Rad2Deg);
            lower.rotation *= Quaternion.AngleAxis(Mathf.Clamp(twist, -90f, 90f), axis);
            // Keep the owned wrist exactly on its anchor at full throw. The reach guard
            // bounds segment extension to eight centimetres; native bones remain untouched.
            hand.position = target;
            hand.rotation = gripRotation;
        }

        private void UpdateHeadMask()
        {
            if (Renderer == null || Head == null || material == null) return;
            float radius = neck != null ? Mathf.Clamp(Vector3.Distance(Head.position, neck.position) * 2.2f, .18f, .25f) : .22f;
            Vector3 center = Head.position;
            properties.SetVector(HeadCenterId, new Vector4(center.x, center.y, center.z, radius));
            if (headEnd != null)
            {
                float span = Mathf.Clamp(Vector3.Distance(Head.position, headEnd.position), .16f, .26f);
                properties.SetMatrix(HeadMaskId, Matrix4x4.TRS((Head.position + headEnd.position) * .5f,
                    Head.rotation, new Vector3(span * .75f, span * .80f, span * .85f)).inverse);
                properties.SetFloat(HeadModeId, 2);
            }
            else properties.SetFloat(HeadModeId, 1);
            Renderer.SetPropertyBlock(properties);
        }

        internal void SetBodyVisible(bool value)
        {
            bodyVisible = value;
            // The full renderer must keep skinning for explicit reflection draws. The
            // headless body can skip camera culling/submission until its bound camera.
            if (firstPerson != null) firstPerson.enabled = false;
            if (material != null) material.SetFloat(BodyVisibleId, 0f);
            if (bodyMaterial != null) bodyMaterial.SetFloat(BodyVisibleId, 0f);
        }
        internal void SetLight(float light)
        {
            float level = Mathf.Clamp(light, .06f, 1f);
            if (material != null) material.SetFloat(LightId, level);
            if (bodyMaterial != null) bodyMaterial.SetFloat(LightId, level);
        }
        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering) => OnPreCull(rendering);
        private void OnPreCull(Camera rendering)
        {
            bool visible = bodyVisible && rendering == camera;
            if (firstPerson != null) firstPerson.enabled = visible;
            if (material != null) material.SetFloat(BodyVisibleId, visible && firstPerson == null ? 1f : 0f);
            if (bodyMaterial != null) bodyMaterial.SetFloat(BodyVisibleId, visible ? 1f : 0f);
        }

        internal void Release()
        {
            if (hooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                Camera.onPreCull -= OnPreCull;
                hooked = false;
            }
            if (nativeAnimator != null && nativeAnimator.cullingMode == AnimatorCullingMode.AlwaysAnimate)
                nativeAnimator.cullingMode = originalCulling;
            if (root != null) { root.SetActive(false); Object.Destroy(root); }
            if (material != null) Object.Destroy(material);
            if (bodyMaterial != null) Object.Destroy(bodyMaterial);
            for (int i = 0; i < count; i++) { sources[i] = null; copies[i] = null; }
            count = 0; root = null; material = null; camera = null; frame = null; nativeAnimator = null; nativeRenderer = null;
            Head = Chest = neck = headEnd = rightUpper = rightLower = rightHand = leftUpper = leftLower = leftHand = null;
            bodyMaterial = null; firstPerson = null;
            leftFoot = rightFoot = stick = throttle = null; Renderer = null;
            phase = lean = pitchLean = headPitch = headYaw = pedals = 0f;
            bodyVisible = false; stickOnRight = true; properties.Clear();
            rightArmLength = leftArmLength = stickRestFit = throttleRestFit = 0f;
        }
    }
}
