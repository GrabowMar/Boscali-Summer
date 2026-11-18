using System;
using System.Runtime.Serialization;

namespace BoscaliSummer.Modules.Cinematography.Domain
{
    /// <summary>An edit: shots played one after another.</summary>
    [DataContract]
    internal sealed class ShotSequence
    {
        internal const int ClipLimit = 32;
        [DataMember] public ShotPlan[] clips = Array.Empty<ShotPlan>();
        [OnDeserializing] private void Defaults(StreamingContext _) { clips=Array.Empty<ShotPlan>(); }
        internal static bool Validate(ShotSequence sequence,out string error)
        {
            error=null;
            if (sequence?.clips==null || sequence.clips.Length==0 || sequence.clips.Length>ClipLimit) { error="An edit holds 1 to 32 clips"; return false; }
            foreach(var clip in sequence.clips)
                if(!ShotPlan.Validate(clip,out error)) return false;
            return true;
        }
    }
}
