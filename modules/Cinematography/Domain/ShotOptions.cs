using System;
using System.Numerics;
using System.Runtime.Serialization;

namespace BoscaliSummer.Modules.Cinematography.Domain
{
    [DataContract]
    internal sealed class ShotOptions
    {
        [DataMember] public float playbackRate = 1, simulationRate = 1;
        [DataMember] public bool realtimeClock=false, lookAtTarget=false, dollyZoom=false;
        [DataMember] public bool stabilizeAnchor=false;
        [DataMember] public float targetX=0, targetY=0, targetZ=0, referenceDistance = 50;
        [DataMember] public float shakeMeters=0, shakeDegrees=0, rollDegrees=0;
        [DataMember] public int seed=0;
        [DataMember] public float letterbox=0, fadeIn=0, fadeOut=0;
        [DataMember] public string title = "";
        [OnDeserializing] private void Defaults(StreamingContext _) { playbackRate = simulationRate = 1; referenceDistance = 50; title = ""; }

        internal static bool Validate(ShotOptions o, float duration, out string error)
        {
            error = null; if (o == null) return true;
            if (!Range(o.playbackRate,.05f,4) || !Range(o.simulationRate,.05f,1) || duration / o.playbackRate > 600)
                error = "Camera rate 0.05–4, simulation rate 0.05–1; max 600 camera seconds";
            else if (!Range(o.shakeMeters,0,.75f) || !Range(o.shakeDegrees,0,3) || !Range(o.rollDegrees,-20,20) ||
                !Range(o.letterbox,0,.2f) || !Range(o.fadeIn,0,duration/2) || !Range(o.fadeOut,0,duration/2)) error = "Effect bounds exceeded";
            else if (!Range(o.targetX,-10000000,10000000) || !Range(o.targetY,-10000000,10000000) ||
                !Range(o.targetZ,-10000000,10000000) || !Range(o.referenceDistance,.1f,100000)) error = "Invalid focus target/distance";
            else if (o.dollyZoom && !o.lookAtTarget) error = "Dolly zoom requires a focus target";
            else if (o.title != null && (o.title.Length > 128 || o.title.IndexOfAny(new[] { '<','>','\n','\r' }) >= 0)) error = "Title must be plain text, up to 128 characters";
            return error == null;
        }
        private static bool Range(float x, float min, float max) => ShotPlan.Finite(x) && x >= min && x <= max;
        internal float Fade(float seconds, float duration) => Math.Clamp(Math.Max(fadeIn > 0 ? 1-seconds/fadeIn : 0,
            fadeOut > 0 ? 1-(duration-seconds)/fadeOut : 0),0,1);

        internal ShotPose Apply(ShotPose pose, float seconds, ShotPose? anchor)
        {
            Vector3 target = new Vector3(targetX,targetY,targetZ);
            if (anchor.HasValue) target = anchor.Value.Position + Vector3.Transform(target,anchor.Value.Rotation);
            Quaternion rotation = pose.Rotation;
            Vector3 direction = target-pose.Position;
            float distance = direction.Length();
            if (lookAtTarget && distance > .001f) rotation = Look(direction);
            float fov = pose.Fov;
            if (dollyZoom && distance > .001f) fov = Math.Clamp(2*MathF.Atan(MathF.Tan(fov*MathF.PI/360)*referenceDistance/distance)*180/MathF.PI,15,80);
            float phase = (seed % 10000) * .017f;
            var noise = new Vector3(MathF.Sin(seconds*7.13f+phase), MathF.Sin(seconds*9.71f+phase+1), MathF.Sin(seconds*5.31f+phase+2));
            Vector3 position = pose.Position + Vector3.Transform(noise*shakeMeters,rotation);
            Quaternion shake = Quaternion.CreateFromYawPitchRoll(noise.X*shakeDegrees*MathF.PI/180,
                noise.Y*shakeDegrees*MathF.PI/180,(rollDegrees+noise.Z*shakeDegrees)*MathF.PI/180);
            return new ShotPose(position,Quaternion.Normalize(rotation*shake),fov);
        }

        internal static Quaternion Look(Vector3 direction)
        {
            Vector3 forward = Vector3.Normalize(direction);
            Vector3 up = Math.Abs(Vector3.Dot(forward,Vector3.UnitY)) > .999f ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 right = Vector3.Normalize(Vector3.Cross(up,forward)); up = Vector3.Cross(forward,right);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                right.X,right.Y,right.Z,0, up.X,up.Y,up.Z,0, forward.X,forward.Y,forward.Z,0, 0,0,0,1)));
        }
    }
}
