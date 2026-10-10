using System;
using System.Runtime.Serialization;

namespace BoscaliSummer.Modules.Cinematography.Domain
{
    [DataContract]
    internal sealed class ShotSequence
    {
        internal const int ClipLimit = 32;
        [DataMember] public int version = 1;
        [DataMember] public string id = "edit01";
        [DataMember] public ShotPlan[] clips = Array.Empty<ShotPlan>();
        internal float LengthSeconds
        { get { float seconds=0; foreach (var clip in clips) seconds+=clip.duration/(clip.options?.playbackRate ?? 1); return seconds; } }
        internal static bool Validate(ShotSequence sequence,out string error)
        {
            error=null;
            if (sequence==null || sequence.version!=1 || !ShotPlan.SafeId(sequence.id) || sequence.clips==null ||
                sequence.clips.Length==0 || sequence.clips.Length>ClipLimit) { error="Edit requires valid ID/version and 1–32 clips"; return false; }
            string mission=null,scene=null;
            foreach(var clip in sequence.clips)
            {
                if(!ShotPlan.Validate(clip,out error)) return false;
                if(mission!=null && (clip.mission!=mission || clip.scene!=scene)) { error="Edit clips must share mission/scene";return false; }
                mission=clip.mission;scene=clip.scene;
            }
            if(sequence.LengthSeconds>600) { error="Edit exceeds 600 camera seconds";return false; }
            return true;
        }
    }
}
