#if UNITY_2022_3_OR_NEWER
using UnityEngine;

// Native boundary substitutes only. The renderer, shaders and three sun presenters are production source.
public sealed class LevelInfo : MonoBehaviour
{
    public Light sun;
    public GameObject sunObject;
}
public static class PhysicsLayers { public static int StaticsMask = 1 << 29; }
public static class SunFixtureGlobalPosition
{
    public static Vector3 GlobalPosition(this Transform transform) => transform.position;
}
namespace BoscaliSummer.Core.Game
{
    internal static class GameAccess
    {
        internal static bool TryGetNightVisionState(out bool selected, out bool on)
        { selected = on = false; return true; }
    }
}
namespace BoscaliSummer.Modules.Weather.Visuals
{
    internal static class CloudLowRes
    {
        internal static readonly int CoverId = Shader.PropertyToID("_BoscaliCloudCover");
        internal static readonly int CoverFrameId = Shader.PropertyToID("_BoscaliCloudCoverFrame");
    }
    internal static class CanopyShaderBundle
    {
        internal static Shader GetSunShaftsShader() => Resources.Load<Shader>("SunShafts");
        internal static Shader GetSunDiscShader() => Resources.Load<Shader>("SunDisc");
    }
}
#endif
