#if UNITY_2022_3_OR_NEWER
using UnityEngine;

public sealed class LevelInfo : MonoBehaviour
{
    private CloudLayer cloudLayer;
    public Light sun, moon;
    public float cloudHeight = 1800f;
    public void SetCloudLayer(CloudLayer value) => cloudLayer = value;
}

public static class Datum { public static float LocalSeaY; }
public readonly struct GlobalPosition
{
    public readonly double x, y, z;
    public GlobalPosition(Vector3 value) { x = value.x; y = value.y; z = value.z; }
}
namespace BoscaliSummer.Core.Game
{
    public static class FixtureCoordinates
    {
        public static GlobalPosition GlobalPosition(this Transform value) => new GlobalPosition(value.position);
    }
}

public sealed class CloudLayer : MonoBehaviour
{
    private ParticleSystem cloudSystem;
    private ParticleSystem distantCloudSystem;
    private ParticleSystem flyThroughSystem;
    private MeshRenderer cloudRenderer;
    private Lightning lightning;
    private float cloudSizeMin = 300f;
    private float cloudSizeMax = 500f;
    private float densityMapScale = 250f;
    private float layerThickness = 150f;
    private int maxParticles = 100;
    public void SetCloudSystem(ParticleSystem value) => cloudSystem = value;
    public void SetDistantSystem(ParticleSystem value) => distantCloudSystem = value;
    public void SetFlyThroughSystem(ParticleSystem value) => flyThroughSystem = value;
    public void SetCloudRenderer(MeshRenderer value) => cloudRenderer = value;
    public void SetLightning(Lightning value) => lightning = value;
    public float SizeMin => cloudSizeMin;
    public float SizeMax => cloudSizeMax;
    public float MapScale => densityMapScale;
    public float Thickness => layerThickness;
    public int ParticleLimit => maxParticles;
}

public sealed class Lightning : MonoBehaviour
{
    private ParticleSystem lightningSystem;
    private Light flashLight;
    public void SetEffects(ParticleSystem particles, Light flash)
    {
        lightningSystem = particles;
        flashLight = flash;
    }
}

public static class PlayerSettings
{
    public static Graphics graphics = new Graphics();
    public sealed class Graphics { public float CloudDetail = 1f; }
}

public sealed class SoundManager
{
    public static SoundManager i;
    public UnityEngine.Audio.AudioMixerGroup EffectsMixer;
}
#endif
