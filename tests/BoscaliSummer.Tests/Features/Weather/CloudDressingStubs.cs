#if UNITY_2022_3_OR_NEWER
using UnityEngine;

public sealed class LevelInfo : MonoBehaviour
{
    private CloudLayer cloudLayer;
    public void SetCloudLayer(CloudLayer value) => cloudLayer = value;
}

public sealed class CloudLayer : MonoBehaviour
{
    private ParticleSystem cloudSystem;
    private ParticleSystem flyThroughSystem;
    private float cloudSizeMin = 300f;
    private float cloudSizeMax = 500f;
    private float densityMapScale = 250f;
    private float layerThickness = 150f;
    private int maxParticles = 100;
    public void SetCloudSystem(ParticleSystem value) => cloudSystem = value;
    public void SetFlyThroughSystem(ParticleSystem value) => flyThroughSystem = value;
    public float SizeMin => cloudSizeMin;
    public float SizeMax => cloudSizeMax;
    public float MapScale => densityMapScale;
    public float Thickness => layerThickness;
    public int ParticleLimit => maxParticles;
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
