using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Ui
{
    internal enum CamFeedMode : byte
    {
        Target = 0,
        Missile = 1,
        DroneEo = 2,
        DroneIr = 3,
    }

    /// <summary>
    /// Single-pass lightweight UI effect (<c>NOA/UI/CamFeed</c>) shared by TARGET CAM, MISSILE CAM, and DRONE CAM.
    /// Fails closed (leaves the <see cref="RawImage"/> on its default material) when the shader bundle is absent.
    /// </summary>
    internal sealed class CamFeedEffect
    {
        public const string ShaderName = "NOA/UI/CamFeed";

        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int ThermalId = Shader.PropertyToID("_Thermal");
        private static readonly int SignalId = Shader.PropertyToID("_Signal");
        private static readonly int UvRectId = Shader.PropertyToID("_UvRect");

        private Material mat;
        private CamFeedMode mode;
        private float thermal;
        private float signal = 1f;
        private Vector4 uvRect = new Vector4(0f, 0f, 1f, 1f);

        public CamFeedEffect(CamFeedMode mode) => this.mode = mode;

        public bool Ready => EnsureMaterial();

        public void Bind(RawImage image)
        {
            if (image == null || !EnsureMaterial()) return;
            if (!ReferenceEquals(image.material, mat)) image.material = mat;
        }

        public void SetMode(CamFeedMode next)
        {
            if (mode == next) return;
            mode = next;
            if (mat != null) mat.SetFloat(ModeId, (float)mode);
        }

        public void SetThermal(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Abs(thermal - clamped) <= 0.001f) return;
            thermal = clamped;
            if (mat != null) mat.SetFloat(ThermalId, thermal);
        }

        public void SetSignal(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Abs(signal - clamped) <= 0.001f) return;
            signal = clamped;
            if (mat != null) mat.SetFloat(SignalId, signal);
        }

        public void SetUvRect(Rect uv)
        {
            Vector4 next = new Vector4(
                uv.x,
                uv.y,
                uv.width > 0.001f ? 1f / uv.width : 1f,
                uv.height > 0.001f ? 1f / uv.height : 1f);
            if (next == uvRect) return;
            uvRect = next;
            if (mat != null) mat.SetVector(UvRectId, uvRect);
        }

        public void Dispose()
        {
            if (mat == null) return;
            if (Application.isPlaying) Object.Destroy(mat);
            else Object.DestroyImmediate(mat);
            mat = null;
        }

        private bool EnsureMaterial()
        {
            if (mat != null) return true;
            Shader shader = AvBundle.Shader(ShaderName);
            if (shader == null) return false;
            mat = new Material(shader) { name = "BoscaliCamFeed", hideFlags = HideFlags.HideAndDontSave };
            mat.SetFloat(ModeId, (float)mode);
            mat.SetFloat(ThermalId, thermal);
            mat.SetFloat(SignalId, signal);
            mat.SetVector(UvRectId, uvRect);
            return true;
        }
    }
}
