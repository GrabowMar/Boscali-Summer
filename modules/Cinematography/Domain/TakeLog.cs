using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BoscaliSummer.Modules.Cinematography.Domain
{
    [DataContract]
    internal sealed class TakeBookmark
    {
        [DataMember] public string label;
        [DataMember] public float seconds;
        [DataMember] public string source = "OPERATOR";
    }

    [DataContract]
    internal sealed class TakeEdit
    {
        [DataMember] public string action, data;
        [DataMember] public float seconds;
    }
    [DataContract]
    internal sealed class TakeLog
    {
        internal const int BookmarkLimit = 512;
        private readonly List<TakeBookmark> pending = new List<TakeBookmark>(BookmarkLimit);
        private bool finished;
        private readonly List<TakeEdit> editList=new List<TakeEdit>(128);
        [DataMember] public int version = 1;
        [DataMember] public string id, shotId, mission, scene, build, startedUtc;
        [DataMember] public string status = "Playing", reason = "", recorder = "UNKNOWN";
        [DataMember] public float elapsed;
        [DataMember] public int evaluatedFrames = 0, width = 0, height = 0;
        [DataMember] public TakeBookmark[] bookmarks = Array.Empty<TakeBookmark>();
        [DataMember] public ShotPlan plan;
        [DataMember] public string sequenceId;
        [DataMember] public int clipIndex;
        [DataMember] public TakeEdit[] edits=Array.Empty<TakeEdit>();
        internal bool IsFinished => finished;
        internal int BookmarkCount => pending.Count;
        internal bool CanRecordEdits => !finished && editList.Count<128;

        internal TakeLog(string takeId, ShotPlan shot, string buildIdentity)
        {
            id = takeId; shotId = shot.id; mission = shot.mission; scene = shot.scene; build = buildIdentity;
            startedUtc = DateTime.UtcNow.ToString("O");
            plan = shot;
        }

        internal bool Mark(string label, float seconds)
        {
            if (finished || pending.Count >= BookmarkLimit || string.IsNullOrWhiteSpace(label) || label.Length > 64 ||
                !ShotPlan.Finite(seconds) || seconds < 0 || seconds > plan.duration) return false;
            pending.Add(new TakeBookmark { label = label, seconds = seconds });
            return true;
        }

        internal void Finish(string outcome, string why, float seconds)
        {
            if (finished) return;
            finished = true; status = outcome; reason = why ?? "";
            elapsed = ShotPlan.Finite(seconds) ? Math.Clamp(seconds, 0, plan.duration) : 0;
            bookmarks = pending.ToArray();
            edits=editList.ToArray();
        }
        internal bool RecordEdit(string action,float seconds,string data)
        {
            if(!CanRecordEdits || !ShotPlan.SafeId(action) || !ShotPlan.Finite(seconds) || seconds<0 ||
                seconds>plan.duration || data==null || data.Length>2048) return false;
            editList.Add(new TakeEdit { action=action,seconds=seconds,data=data }); return true;
        }
    }
}
