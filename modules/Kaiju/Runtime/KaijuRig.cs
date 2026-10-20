using BoscaliSummer.Modules.Kaiju.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Kaiju.Runtime
{
    /// <summary>
    /// Facts about the kaiju model (Kaiju.fbx, native scale, metres, model space = bind pose, +Z forward): the weak spots
    /// measured on the body surface in the Unity MCP editor, the capsule colliders, and the clip timings, which mirror
    /// modules/Kaiju/Assets/Editor/KaijuAssetsBuilder.cs.
    /// </summary>
    internal static class KaijuRig
    {
        internal readonly struct Spot
        {
            public readonly string Name, Bone;
            public readonly Vector3 Bind;
            public readonly Weakspot Kind;
            public readonly int Side;
            public readonly float Radius;

            public Spot(string name, string bone, Vector3 bind, Weakspot kind, int side, float radius)
            {
                Name = name; Bone = bone; Bind = bind; Kind = kind; Side = side; Radius = radius;
            }
        }

        public static readonly Spot[] Spots =
        {
            new Spot("THROAT", "Neck3_M", new Vector3(0f, 40.1f, 27.4f), Weakspot.Throat, 0, 3f),
            new Spot("EYE L", "Eye_L", new Vector3(-3f, 46.2f, 31.6f), Weakspot.Eye, -1, 1.6f),
            new Spot("EYE R", "Eye_R", new Vector3(3f, 46.2f, 31.6f), Weakspot.Eye, 1, 1.6f),
            new Spot("SPINE SAC 1", "Neck0_M", new Vector3(0f, 53.6f, 12.6f), Weakspot.Sac, 0, 3.2f),
            new Spot("SPINE SAC 2", "Chest_M", new Vector3(0f, 54.7f, 8.9f), Weakspot.Sac, 0, 3.2f),
            new Spot("SPINE SAC 3", "Spine2_M", new Vector3(0f, 53.2f, 4.1f), Weakspot.Sac, 0, 3.2f),
            new Spot("SPINE SAC 4", "Spine1_M", new Vector3(0f, 51.1f, 0f), Weakspot.Sac, 0, 3.2f),
            new Spot("SPINE SAC 5", "Root_M", new Vector3(0f, 41.1f, -7.6f), Weakspot.Sac, 0, 3.2f),
            new Spot("SPINE SAC 6", "Tail0_M", new Vector3(0f, 37.2f, -14.4f), Weakspot.Sac, 0, 3.2f),
            new Spot("SHOULDER L", "Shoulder_L", new Vector3(-16.1f, 44.7f, 14.6f), Weakspot.Shoulder, -1, 3.5f),
            new Spot("SHOULDER R", "Shoulder_R", new Vector3(16.1f, 44.7f, 14.6f), Weakspot.Shoulder, 1, 3.5f),
            new Spot("ELBOW L", "Elbow_L", new Vector3(-20.7f, 26.3f, 10.6f), Weakspot.Elbow, -1, 3f),
            new Spot("ELBOW R", "Elbow_R", new Vector3(20.7f, 26.3f, 10.6f), Weakspot.Elbow, 1, 3f),
            new Spot("WRIST L", "Wrist_L", new Vector3(-22.3f, 11.6f, 20.1f), Weakspot.Wrist, -1, 3f),
            new Spot("WRIST R", "Wrist_R", new Vector3(22.3f, 11.6f, 20.1f), Weakspot.Wrist, 1, 3f),
            new Spot("KNEE L", "Knee_L", new Vector3(-10.9f, 8.6f, -6.3f), Weakspot.Knee, -1, 2.8f),
            new Spot("KNEE R", "Knee_R", new Vector3(10.9f, 8.6f, -6.3f), Weakspot.Knee, 1, 2.8f),
            new Spot("ANKLE L", "Ankle_L", new Vector3(-12.7f, 4.9f, -5.5f), Weakspot.Ankle, -1, 2.5f),
            new Spot("ANKLE R", "Ankle_R", new Vector3(12.7f, 4.9f, -5.5f), Weakspot.Ankle, 1, 2.5f),
            new Spot("TAIL ROOT", "Tail2_M", new Vector3(0f, 25.4f, -28.7f), Weakspot.Tail, 0, 3f),
            new Spot("TAIL BARB", "joint2_M", new Vector3(0f, 26.4f, -58.6f), Weakspot.Tail, 0, 2.5f),
            new Spot("HEART", "Chest_M", new Vector3(0f, 35.9f, 16.6f), Weakspot.Heart, 0, 4.5f),
        };

        public static int HeartIndex => Spots.Length - 1;

        /// <summary>Solid body: capsule from bone to bone, radius in native metres.</summary>
        public static readonly (string from, string to, float radius)[] Segments =
        {
            ("Root_M", "Chest_M", 10f), ("Chest_M", "Neck0_M", 9f), ("Neck0_M", "Neck3_M", 6f), ("Neck3_M", "Head_M", 5f),
            ("Head_M", "HeadEnd_M", 4.5f), ("Jaw_M", "JawEnd_M", 3f),
            ("Shoulder_L", "Elbow_L", 4.5f), ("Elbow_L", "Wrist_L", 3.5f), ("Wrist_L", "MiddleFinger2_L", 3f),
            ("Shoulder_R", "Elbow_R", 4.5f), ("Elbow_R", "Wrist_R", 3.5f), ("Wrist_R", "MiddleFinger2_R", 3f),
            ("Hip_L", "Knee_L", 5f), ("Knee_L", "Ankle_L", 3.5f), ("Ankle_L", "Toes_L", 3f),
            ("Hip_R", "Knee_R", 5f), ("Knee_R", "Ankle_R", 3.5f), ("Ankle_R", "Toes_R", 3f),
            ("Tail0_M", "Tail2_M", 5f), ("Tail2_M", "Tail4_M", 4f), ("Tail4_M", "joint1_M", 3f), ("joint1_M", "joint3_M", 2f),
        };

        /// <summary>Walk-cycle footfalls (clip phase when each foot lands) and the bone that lands.</summary>
        public static readonly (float phase, string bone)[] Footfalls =
            { (0.35f, "Toes_L"), (0.60f, "Wrist_L"), (0.85f, "Toes_R"), (0.10f, "Wrist_R") };

        public const float SpikeRelease = 1.55f, SwipeHitStart = 0.95f, SwipeHitEnd = 1.35f; // clip seconds

        public static string State(KaijuAction a) => a == KaijuAction.Rise ? "Walk" : a.ToString();

        /// <summary>Animator speed: scale makes the authored timing too brisk for a mountain, so most actions run slow.</summary>
        public static float Playback(KaijuAction a)
        {
            switch (a)
            {
                case KaijuAction.Walk:
                case KaijuAction.Rise: return 0.8f;
                case KaijuAction.Roar:
                case KaijuAction.Spike:
                case KaijuAction.Flinch: return 0.8f;
                case KaijuAction.SwipeL:
                case KaijuAction.SwipeR: return 0.55f;
                case KaijuAction.Death: return 0.6f;
                default: return 1f;
            }
        }

        /// <summary>Real seconds an action lasts; Walk/Idle/Rise are open-ended (0).</summary>
        public static float Duration(KaijuAction a)
        {
            float clip;
            switch (a)
            {
                case KaijuAction.Roar: clip = 4.5f; break;
                case KaijuAction.LaserCharge: clip = 3f; break;
                case KaijuAction.LaserFire: return KaijuRules.LaserFireSeconds;
                case KaijuAction.Spike: clip = 3.2f; break;
                case KaijuAction.SwipeL:
                case KaijuAction.SwipeR: clip = 2.6f; break;
                case KaijuAction.Flinch: clip = 1.2f; break;
                case KaijuAction.Death: clip = 6f; break;
                default: return 0f;
            }
            return clip / Playback(a);
        }
    }
}
