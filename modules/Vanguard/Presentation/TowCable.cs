using System.Collections.Generic;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: draws the ALE-X fiber from the pod's exit point to the decoy from the frame the decoy exists
    /// (TowAnchor.Capture calls Track), and drops it the moment the kite is cut. Runs after the floating-origin
    /// shift (CameraStateManager.LateUpdate, order 2) so the line never flashes.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class TowCable : MonoBehaviour, ISceneService
    {
        private static readonly List<Missile> Pending = new List<Missile>();
        private readonly Dictionary<Missile, LineRenderer> lines = new Dictionary<Missile, LineRenderer>();
        private readonly List<Missile> gone = new List<Missile>();
        private Material material;

        public static void Track(Missile decoy)
        {
            if (decoy != null && !Pending.Contains(decoy)) Pending.Add(decoy);
        }

        public void ResetForScene()
        {
            Pending.Clear();
            foreach (LineRenderer line in lines.Values)
                if (line != null) Destroy(line.gameObject);
            lines.Clear();
            if (material != null) Destroy(material);
            material = null;
        }

        private void LateUpdate()
        {
            for (int i = 0; i < Pending.Count; i++) Open(Pending[i]);
            Pending.Clear();
            if (lines.Count == 0) return;
            gone.Clear();
            foreach (KeyValuePair<Missile, LineRenderer> pair in lines)
            {
                Missile decoy = pair.Key;
                Unit host = decoy != null ? decoy.owner : null;
                if (decoy == null || decoy.disabled || host == null || host.disabled)
                {
                    gone.Add(decoy);
                    continue;
                }
                // No view yet (a client before the first AlexState) counts as on the fiber.
                bool onFiber = !AlexBoard.Views.TryGetValue(decoy.persistentID.Id, out AlexView view) ||
                    view.State == KiteState.Deploy || view.State == KiteState.Towed;
                pair.Value.enabled = onFiber;
                if (!onFiber) continue;
                pair.Value.SetPosition(0, TowAnchor.RootFor(decoy, host));
                pair.Value.SetPosition(1, decoy.transform.position);
            }
            foreach (Missile m in gone)
            {
                if (lines[m] != null) Destroy(lines[m].gameObject);
                lines.Remove(m);
            }
        }

        private void Open(Missile decoy)
        {
            if (decoy == null || decoy.disabled || lines.ContainsKey(decoy)) return;
            if (material == null)
            {
                // UnityEngine.Object overloads ==, so no ??: a headless server has neither shader and draws nothing.
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                material = new Material(shader) { color = new Color(0.42f, 0.45f, 0.48f) };
            }
            var go = new GameObject("ALE-X fiber");
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = 0.06f;
            line.useWorldSpace = true;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            lines[decoy] = line;
        }
    }
}
