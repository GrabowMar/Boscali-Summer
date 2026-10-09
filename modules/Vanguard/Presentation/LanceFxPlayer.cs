using System.Collections.Generic;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer with a screen: the through-shot streak, a cyan-white line from the first impact to the last pierced
    /// point that fades in 0.45 s. Positions are world-anchored (GlobalPosition) and re-converted every frame after the
    /// floating-origin shift (CameraStateManager.LateUpdate, order 2) so the line never flashes.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class LanceFxPlayer : MonoBehaviour, ISceneService
    {
        private const float Seconds = 0.45f;
        private const int Pool = 4;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color0 = Shader.PropertyToID("_Color");
        private static LanceFxPlayer instance;

        private sealed class BeamLine
        {
            public LineRenderer Line;
            public GlobalPosition A, B;
            public float At = -99f;
        }

        private readonly List<BeamLine> beams = new List<BeamLine>(Pool);
        private Material material;
        private MaterialPropertyBlock block;
        private int next;

        private void Awake() => instance = this;

        private void OnDestroy()
        {
            ResetForScene();
            if (instance == this) instance = null;
        }

        public void ResetForScene()
        {
            foreach (BeamLine beam in beams)
                if (beam.Line != null) Destroy(beam.Line.gameObject);
            beams.Clear();
            if (material != null) Destroy(material);
            material = null;
        }

        public static void Beam(GlobalPosition a, GlobalPosition b)
        {
            if (instance != null && !GameManager.IsHeadless) instance.Add(a, b);
        }

        private void Add(GlobalPosition a, GlobalPosition b)
        {
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                material = new Material(shader);
                block = new MaterialPropertyBlock();
            }
            BeamLine beam;
            if (beams.Count < Pool)
            {
                var go = new GameObject("LanceBeam");
                go.transform.SetParent(transform, false);
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.sharedMaterial = material;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                beams.Add(beam = new BeamLine { Line = line });
            }
            else beam = beams[next++ % Pool]; // overwrite the oldest slot
            beam.A = a;
            beam.B = b;
            beam.At = Time.time;
            beam.Line.enabled = true;
        }

        private void LateUpdate()
        {
            float now = Time.time;
            foreach (BeamLine beam in beams)
            {
                if (beam.Line == null || !beam.Line.enabled) continue;
                float t = (now - beam.At) / Seconds;
                if (t >= 1f)
                {
                    beam.Line.enabled = false;
                    continue;
                }
                float alpha = (1f - t) * (1f - t);
                beam.Line.SetPosition(0, beam.A.ToLocalPosition());
                beam.Line.SetPosition(1, beam.B.ToLocalPosition());
                beam.Line.startWidth = 0.35f * (1f - 0.5f * t);
                beam.Line.endWidth = 0.05f;
                var color = new Color(0.9f * 2.5f, 1.1f * 2.5f, 1.3f * 2.5f, alpha); // HDR white-cyan
                block.SetColor(BaseColor, color);
                block.SetColor(Color0, color);
                beam.Line.SetPropertyBlock(block);
            }
        }
    }
}
