using System.Collections.Generic;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: draws the ALE-X fiber from host tail to decoy. Runs after the floating-origin shift
    /// (CameraStateManager.LateUpdate, order 2) so the line never flashes.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class TowCable : MonoBehaviour, ISceneService
    {
        private readonly Dictionary<Missile, LineRenderer> lines = new Dictionary<Missile, LineRenderer>();
        private readonly List<Missile> gone = new List<Missile>();
        private Material material;
        private float nextScan;

        public void ResetForScene()
        {
            foreach (LineRenderer line in lines.Values)
                if (line != null) Destroy(line.gameObject);
            lines.Clear();
            if (material != null) Destroy(material);
            material = null;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                Scan();
            }
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
                pair.Value.SetPosition(0, Runtime.TowAnchor.RootFor(decoy, host));
                pair.Value.SetPosition(1, decoy.transform.position);
            }
            foreach (Missile m in gone)
            {
                if (lines[m] != null) Destroy(lines[m].gameObject);
                lines.Remove(m);
            }
        }

        // ponytail: FindObjectsOfType at 1 Hz; switch to a spawn hook if missile counts ever get large.
        private void Scan()
        {
            if (material == null)
            {
                // UnityEngine.Object overloads ==, so no ??: a headless server has neither shader and draws nothing.
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                material = new Material(shader) { color = new Color(0.12f, 0.13f, 0.14f) };
            }
            foreach (Missile m in FindObjectsOfType<Missile>())
            {
                if (m.disabled || lines.ContainsKey(m) || m.definition == null ||
                    VanguardKeys.RoleOf(m.definition.jsonKey) != VanguardRole.Towed) continue;
                var go = new GameObject("ALE-X fiber");
                go.transform.SetParent(transform, false);
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.widthMultiplier = 0.05f;
                line.useWorldSpace = true;
                line.sharedMaterial = material;
                lines[m] = line;
            }
        }
    }
}
