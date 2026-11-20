using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>The G-240's exterior lighting rhythm (art: details/detail_lights.py, one object per host and blink
    /// class). Nav red/green and the formation strips burn steady; the white tail/wingtip strobes double-flash like
    /// a transport's; the centreline anti-collision beacons pulse out of phase with them. Deterministic from
    /// Time.time, so every peer blinks the same without a byte on the wire; lamps that rode a shot-off island keep
    /// blinking with it. The strobes are off while the carrier is disabled: a dead ship goes dark.</summary>
    internal sealed class MegaplaneLights : MonoBehaviour
    {
        private const float StrobePeriod = 1.4f;   // double-flash every 1.4 s (transport practice is 1-1.5)
        private const float BeaconPeriod = 0.9f;   // anti-collision pulse
        private Aircraft aircraft;
        private readonly List<Renderer> strobes = new List<Renderer>(), beacons = new List<Renderer>();

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Det_lights_")) continue;
                if (t.name.EndsWith("_strobe")) Add(strobes, t);
                else if (t.name.EndsWith("_beacon")) Add(beacons, t);
            }
        }

        private static void Add(List<Renderer> list, Transform t)
        {
            Renderer r = t.GetComponent<Renderer>();
            if (r != null) list.Add(r);
        }

        private void Update()
        {
            if (aircraft == null) return;
            bool dark = aircraft.disabled;
            float t = Time.time;
            // double-flash: two 60 ms blinks 150 ms apart, then a long dark
            bool strobe = !dark && (t % StrobePeriod < .06f || t % StrobePeriod > .15f && t % StrobePeriod < .21f);
            // anti-collision: a 120 ms pulse, offset a third period so the two rhythms never lock
            bool beacon = !dark && (t + BeaconPeriod / 3f) % BeaconPeriod < .12f;
            Set(strobes, strobe);
            Set(beacons, beacon);
        }

        private static void Set(List<Renderer> list, bool on)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Renderer r = list[i];
                if (r == null) continue;   // its island left with the piece
                if (r.enabled != on) r.enabled = on;
            }
        }
    }
}
