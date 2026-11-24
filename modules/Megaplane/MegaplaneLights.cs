using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Exterior lamps keep their glass and mount visible through each flash and after loss of power.</summary>
    internal sealed class MegaplaneLights : MonoBehaviour
    {
        private const float StrobePeriod = 1.4f, BeaconPeriod = .9f;
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private enum Pattern { Steady, Strobe, Beacon }

        private sealed class Lamp
        {
            internal Renderer Fixture;
            internal AeroPart Owner;
            internal Pattern Rhythm;
            internal int Slot;
            internal MaterialPropertyBlock Unlit;
            internal bool Lit = true;
        }

        private Aircraft aircraft;
        private MegaplaneReactor reactor;
        private readonly List<Lamp> lamps = new List<Lamp>();

        internal static bool IsFixture(string name) => name.StartsWith("Det_lights_", StringComparison.Ordinal) ||
            name.StartsWith("TipLamp_", StringComparison.Ordinal);

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            reactor = GetComponent<MegaplaneReactor>();
            foreach (MeshRenderer fixture in GetComponentsInChildren<MeshRenderer>(true))
            {
                string name = fixture.name;
                if (!IsFixture(name)) continue;
                Pattern rhythm = name.EndsWith("_strobe", StringComparison.Ordinal) ? Pattern.Strobe :
                    name.EndsWith("_beacon", StringComparison.Ordinal) ? Pattern.Beacon : Pattern.Steady;
                AeroPart owner = fixture.GetComponentInParent<AeroPart>(true);
                Material[] materials = fixture.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null || !material.IsKeywordEnabled("_EMISSION")) continue;
                    var unlit = new MaterialPropertyBlock();
                    unlit.SetColor(Emission, Color.black);
                    // An unpowered lens still catches sunlight and retains its tint.
                    int baseId = material.HasProperty(BaseColor) ? BaseColor : ColorId;
                    if (material.HasProperty(baseId))
                    {
                        Color tint = material.GetColor(baseId);
                        Color dim = tint * .18f;
                        dim.a = tint.a;
                        unlit.SetColor(baseId, dim);
                    }
                    lamps.Add(new Lamp { Fixture = fixture, Owner = owner, Slot = slot, Rhythm = rhythm, Unlit = unlit });
                }
            }
        }

        private void Update()
        {
            bool power = aircraft != null && !aircraft.disabled && (reactor == null || reactor.AvailablePower > 0f);
            float now = Time.time;
            float phase = now % StrobePeriod;
            bool strobe = phase < .06f || phase >= .15f && phase < .21f;
            bool beacon = (now + BeaconPeriod / 3f) % BeaconPeriod < .12f;
            foreach (Lamp lamp in lamps)
            {
                bool attached = lamp.Owner != null && !lamp.Owner.IsDetached();
                Set(lamp, power && attached && (lamp.Rhythm == Pattern.Steady ||
                    (lamp.Rhythm == Pattern.Strobe ? strobe : beacon)));
            }
        }

        private void OnDisable()
        {
            // Detached fixtures can outlive the unit which supplied their power.
            foreach (Lamp lamp in lamps) Set(lamp, false);
        }

        private static void Set(Lamp lamp, bool on)
        {
            if (lamp.Lit == on || lamp.Fixture == null) return;
            lamp.Lit = on;
            lamp.Fixture.SetPropertyBlock(on ? null : lamp.Unlit, lamp.Slot);
        }
    }
}
