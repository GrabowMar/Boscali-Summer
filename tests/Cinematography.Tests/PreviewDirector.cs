#if UNITY_EDITOR
using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;
// Offline presentation fixture only; copied as CinematicDirector.cs for Unity's script identity.
namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    internal sealed class CinematicDirector : MonoBehaviour
    {
        internal ShotPlan Plan = new ShotPlan { id = "shot01", mission = "Preview", scene = "Preview",
            keys = new[] { new ShotKey(), new ShotKey { time = 10 } } };
        internal TakeLog Take;
        internal ShotSequence Edit=new ShotSequence { id="clear-window",clips=new[] {
            new ShotPlan {id="ingress",mission="Preview",scene="Preview",duration=12},
            new ShotPlan {id="insertion",mission="Preview",scene="Preview",duration=8},
            new ShotPlan {id="intercept",mission="Preview",scene="Preview",duration=10},
            new ShotPlan {id="exfil",mission="Preview",scene="Preview",duration=15} } };
        internal bool Playing,CameraPaused;
        internal float Elapsed=0;
        internal string Status => "IDLE";
        internal string Message => "POSE CAPTURED · 2 KEYS";
        internal bool CanEdit(out string reason) { reason = null; return true; }
        internal bool CanUseConsole(out string reason) {reason=null;return true;}
        internal void NewShot() { } internal void NextSlot() { } internal void Save() { } internal void Load() { }
        internal void Capture() { } internal void RemoveLast() { } internal void AdjustDuration(float value) { }
        internal void ToggleSmooth() { } internal void Play() { }
        internal void Stop() { }internal bool PauseCamera(bool value) {CameraPaused=value;return true;}
        internal bool Seek(float value) {Elapsed=value;return true;}
        internal void CyclePath() { }internal void EditKey(int i,float t,float f) { }
        internal void AppendClip() { }internal void SaveEdit() { }internal void LoadEdit() { }internal void ClearEdit() { }
        internal void MoveClip(int i,int delta) { }internal void RemoveClip(int i) { }internal bool PlayEdit()=>true;
        internal void Bookmark() { }internal void SetCameraRate(float rate) { }internal void SetSimulationRate(float rate) { }
        internal void SetPreset(string preset) { }internal void ToggleOption(string option) { }
    }
}
#endif
