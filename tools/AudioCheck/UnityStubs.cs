// Only the Unity API surface needed by the real loader/UI classes. No game or sound device is started.
namespace UnityEngine
{
    public class Object
    {
        public string name;
        internal bool destroyed;
        public static void Destroy(Object item)
        {
            if (ReferenceEquals(item, null)) return;
            item.destroyed = true;
            if (item is GameObject host) foreach (Object child in host.components.Values) child.destroyed = true;
        }
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a, null) || a.destroyed) ? ReferenceEquals(b, null) || b.destroyed : ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public sealed class AudioClip : Object
    {
        public static int Created;
        public int samples, channels, frequency;
        public float length => (float)samples / frequency;
        public float[] Data;
        public static AudioClip Create(string name, int samples, int channels, int frequency, bool streaming)
        { Created++; return new AudioClip { name = name, samples = samples, channels = channels, frequency = frequency }; }
        public bool SetData(float[] data, int offset) { Data = data; return true; }
    }
    public sealed class AudioSource : Object
    {
        public static int Played;
        public GameObject gameObject;
        public bool playOnAwake, loop;
        public bool isPlaying;
        public float spatialBlend, volume, pitch;
        public AudioClip clip;
        public Audio.AudioMixerGroup outputAudioMixerGroup;
        public int priority;
        public void PlayOneShot(AudioClip clip, float gain)
        { if (clip == null) throw new Exception("null clip playback"); if (!float.IsFinite(gain)) throw new Exception("invalid gain"); Played++; }
        public void Play() { if (clip == null) throw new Exception("null clip playback"); isPlaying = true; Played++; }
        public void Stop() { isPlaying = false; }
    }
    public sealed class GameObject : Object
    {
        internal readonly Dictionary<Type, Object> components = new();
        public HideFlags hideFlags;
        public GameObject(string name, params Type[] types)
        { this.name = name; foreach (Type type in types) { var item = (Object)Activator.CreateInstance(type); components[type] = item; if (item is AudioSource audio) audio.gameObject = this; } }
        public T GetComponent<T>() where T : Object => (T)components[typeof(T)];
        public T AddComponent<T>() where T : Object, new()
        { var item = new T(); components[typeof(T)] = item; if (item is AudioSource audio) audio.gameObject = this; return item; }
    }
    public enum HideFlags { HideAndDontSave }
    public static class Time { public static float unscaledTime; }
    public static class Mathf
    {
        public static float Clamp01(float n) => Math.Clamp(n, 0f, 1f);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int RoundToInt(float n) => (int)Math.Round(n);
    }
}
namespace UnityEngine.Audio { public sealed class AudioMixerGroup : UnityEngine.Object { } }
public sealed class SoundManager
{
    public static SoundManager i = new();
    public UnityEngine.Audio.AudioMixerGroup InterfaceMixer = new(), EffectsMixer = new();
}
namespace BoscaliSummer.Core.Fx
{
    internal static class FxBus { internal static VoiceScales Scales => new(); }
    internal sealed class VoiceScales { internal float Voices => 1f; }
}
namespace BoscaliSummer
{
    internal static class Plugin { internal static readonly Logger Logger = new(); }
    internal sealed class Logger { internal void LogWarning(string value) { } }
}
