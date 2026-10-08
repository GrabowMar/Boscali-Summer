using System;
using System.Numerics;
using System.Runtime.Serialization;

namespace BoscaliSummer.Modules.Cinematography.Domain
{
    [DataContract]
    internal sealed class ShotKey
    {
        [DataMember] public float time;
        [DataMember] public float x = 0, y = 0, z = 0;
        [DataMember] public float qX = 0, qY = 0, qZ = 0, qW = 1;
        [DataMember] public float fov = 45;
        internal Vector3 Position => new Vector3(x, y, z);
        internal Quaternion Rotation => new Quaternion(qX, qY, qZ, qW);
        [OnDeserializing] private void Defaults(StreamingContext _) { qW=1;fov=45; }
    }

    internal readonly struct ShotPose
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly float Fov;
        internal ShotPose(Vector3 position, Quaternion rotation, float fov)
        { Position = position; Rotation = rotation; Fov = fov; }
    }

    [DataContract]
    internal sealed class ShotPlan
    {
        internal const int KeyLimit = 64;
        [DataMember] public int version = 1;
        [DataMember] public string id;
        [DataMember] public string mission;
        [DataMember] public string scene;
        [DataMember] public float duration = 10;
        [DataMember] public bool smooth;
        [DataMember] public bool spline;
        [DataMember(EmitDefaultValue=false)] public string anchorId;
        [DataMember(EmitDefaultValue=false)] public ShotOptions options;
        // All positions are global sea-level coordinates; never persist Unity-local poses.
        [DataMember] public ShotKey[] keys = Array.Empty<ShotKey>();
        [OnDeserializing] private void Defaults(StreamingContext _) { version=1;duration=10;keys=Array.Empty<ShotKey>(); }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool SafeId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return false;
            }
            return true;
        }

        internal static bool Validate(ShotPlan plan, out string error)
        {
            error = null;
            if (plan == null || plan.version != 1) error = "Unsupported shot version";
            else if (!SafeId(plan.id)) error = "Invalid shot ID";
            else if (string.IsNullOrWhiteSpace(plan.mission) || plan.mission.Length > 256 ||
                string.IsNullOrWhiteSpace(plan.scene) || plan.scene.Length > 256) error = "Missing mission/scene identity";
            else if (!Finite(plan.duration) || plan.duration < .5f || plan.duration > 120) error = "Duration must be 0.5–120 seconds";
            else if (plan.keys == null || plan.keys.Length == 0 || plan.keys.Length > KeyLimit) error = "Shot requires 1–64 keys";
            if (error != null) return false;
            if (anchorIdInvalid(plan.anchorId)) { error="Actor identity must be 1–256 characters";return false; }
            if (!ShotOptions.Validate(plan.options,plan.duration,out error)) return false;
            float previous = -1;
            for (int i = 0; i < plan.keys.Length; i++)
            {
                ShotKey key = plan.keys[i];
                if (key == null || !Finite(key.time) || key.time < 0 || key.time > plan.duration || key.time <= previous)
                { error = "Keys must have finite increasing times"; return false; }
                float norm = key.Rotation.LengthSquared();
                if (!Finite(key.x) || !Finite(key.y) || !Finite(key.z) || Math.Abs(key.x) > 10000000 ||
                    Math.Abs(key.y) > 10000000 || Math.Abs(key.z) > 10000000 || !Finite(norm) || Math.Abs(norm - 1) > .01f)
                { error = "Invalid pose or rotation"; return false; }
                if (!Finite(key.fov) || key.fov < 15 || key.fov > 80)
                { error = "Vertical FOV must be 15–80 degrees"; return false; }
                previous = key.time;
            }
            if (plan.keys[0].time != 0 || plan.keys.Length > 1 && plan.keys[plan.keys.Length - 1].time != plan.duration)
            { error = "Keys must cover the shot endpoints"; return false; }
            return true;
        }

        // ponytail: at most 64 keys; linear segment lookup is cheaper to own than an index.
        internal ShotPose Sample(float seconds)
        {
            ShotKey first = keys[0];
            if (keys.Length == 1 || seconds <= 0) return Pose(first);
            int end = 1;
            while (end < keys.Length - 1 && keys[end].time < seconds) end++;
            ShotKey a = keys[end - 1], b = keys[end];
            float t = Math.Clamp((seconds - a.time) / (b.time - a.time), 0, 1);
            if (smooth) t = t * t * (3 - 2 * t);
            Vector3 position=Vector3.Lerp(a.Position,b.Position,t);
            if(spline && keys.Length>2)
            {
                Vector3 p0=end>1?keys[end-2].Position:2*a.Position-b.Position;
                Vector3 p3=end+1<keys.Length?keys[end+1].Position:2*b.Position-a.Position;
                position=ShotRigs.Curve(p0,a.Position,b.Position,p3,t);
            }
            return new ShotPose(position,
                Quaternion.Normalize(Quaternion.Slerp(a.Rotation, b.Rotation, t)), a.fov + (b.fov - a.fov) * t);
        }

        private static ShotPose Pose(ShotKey key) => new ShotPose(key.Position, Quaternion.Normalize(key.Rotation), key.fov);
        private static bool anchorIdInvalid(string id) => id!=null && (string.IsNullOrWhiteSpace(id)||id.Length>256);
        internal ShotPose SamplePresentation(float seconds,ShotPose? anchor=null)
        {
            if(anchor.HasValue && options?.stabilizeAnchor==true)
                anchor=new ShotPose(anchor.Value.Position,Quaternion.Identity,anchor.Value.Fov);
            ShotPose pose=Sample(seconds);
            if(anchor.HasValue) pose=new ShotPose(anchor.Value.Position+Vector3.Transform(pose.Position,anchor.Value.Rotation),
                Quaternion.Normalize(anchor.Value.Rotation*pose.Rotation),pose.Fov);
            return options==null?pose:options.Apply(pose,seconds,anchor);
        }
    }
}
