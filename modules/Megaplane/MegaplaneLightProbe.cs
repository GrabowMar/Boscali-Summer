#if !PUBLIC_RELEASE
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Sample the lamp actually being drawn on each wing, including after its native owner separates.</summary>
    public sealed class MegaplaneLightProbe : MonoBehaviour
    {
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private MeshRenderer port, starboard, strobe;
        private bool sampledOn, sampledOff, visibleThroughout;

        public static object Cabin(Dictionary<string, object> args)
        {
            var carrier = (Aircraft)args["idUnit"];
            var lamps = carrier.GetComponentsInChildren<Light>(true).Where(l => l.name == "Cabin downlight").ToArray();
            var liner = carrier.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "FlightDeck_CabinLiner");
            var glow = new MaterialPropertyBlock();
            int slot = liner == null ? -1 : System.Array.FindIndex(liner.sharedMaterials, m => m.name == "BS_Megaplane_CrewDome_Emit");
            if (slot >= 0) liner.GetPropertyBlock(glow, slot);
            return new { installed = lamps.Length, illuminated = lamps.Count(l => l.isActiveAndEnabled),
                lensesGlowing = glow.GetColor("_EmissionColor").maxColorComponent > .01f };
        }

        public static object Read(Dictionary<string, object> args)
        {
            var carrier = (Aircraft)args["idUnit"];
            var probe = carrier.GetComponent<MegaplaneLightProbe>();
            if (probe == null)
            {
                probe = carrier.gameObject.AddComponent<MegaplaneLightProbe>();
                MeshRenderer[] meshes = carrier.GetComponentsInChildren<MeshRenderer>(true);
                probe.port = meshes.Single(r => r.name == "TipLamp_L");
                probe.starboard = meshes.Single(r => r.name == "TipLamp_R");
                probe.strobe = meshes.Single(r => r.name == "Det_lights_tipL_strobe");
            }
            if (args.ContainsKey("sample"))
            {
                probe.StopAllCoroutines();
                probe.StartCoroutine(probe.Observe());
            }
            return new { probe.sampledOn, probe.sampledOff, probe.visibleThroughout,
                portOn = probe.Glow(probe.port), starboardOn = probe.Glow(probe.starboard),
                portSeparated = probe.port.GetComponentInParent<AeroPart>(true).IsDetached(),
                starboardSeparated = probe.starboard.GetComponentInParent<AeroPart>(true).IsDetached() };
        }

        private bool Glow(MeshRenderer fixture)
        {
            // Both wingtip fixtures have a structural housing in the first slot and a glass lens in the second.
            fixture.GetPropertyBlock(block, 1);
            Color emission = block.isEmpty ? fixture.sharedMaterials[1].GetColor("_EmissionColor") : block.GetColor("_EmissionColor");
            return fixture.enabled && !fixture.forceRenderingOff && emission.maxColorComponent > .01f;
        }

        private IEnumerator Observe()
        {
            sampledOn = sampledOff = false;
            visibleThroughout = true;
            float end = Time.time + 2.85f;
            while (Time.time < end)
            {
                bool glowing = Glow(strobe);
                sampledOn |= glowing;
                sampledOff |= !glowing;
                strobe.GetPropertyBlock(block, 0);
                visibleThroughout &= strobe.enabled && !strobe.forceRenderingOff && block.isEmpty;
                yield return null;
            }
        }
    }
}
#endif
