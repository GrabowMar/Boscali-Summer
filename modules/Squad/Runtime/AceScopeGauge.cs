using System;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Squad.Runtime
{
    /// <summary>
    /// The ACE HUNT needle of the TAC BOARD's gauge row (HUD v8): the bearing to the ace leading the wing hunting the local pilot, relative to the nose, with its callsign
    /// and the range under it (caution inside eight kilometres). It outranks every idle gauge while a hunt is on. The hunts and their aircraft live on the host, so the needle
    /// shows on the host and in solo only; a client is told of the hunt (the ticker, the SQD page) but has no position to point at.
    /// </summary>
    internal sealed class AceScopeGauge
    {
        private const float CautionM = 8000f;

        private readonly SquadManager manager;
        private readonly char[] scratch = new char[24];
        private string distance;
        private int distanceKey = int.MinValue;

        public AceScopeGauge(SquadManager manager) { this.manager = manager; }

        public void Register() => ScopeGauges.Register("ace", Read);

        public bool Read(out GaugeSpec gauge)
        {
            gauge = default;
            if (manager == null || !GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled || !manager.TryAce(out Aircraft ace, out string callsign)) return false;
            Vector3 offset = ace.transform.position - me.transform.position, forward = me.transform.forward;
            float bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg, heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float metres = offset.magnitude;
            AvUnits units = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric ? AvUnits.Metric : AvUnits.Imperial;
            int key = (int)(metres / 100f) * 2 + (units == AvUnits.Imperial ? 1 : 0);
            if (key != distanceKey)
            {
                distanceKey = key;
                distance = new string(scratch, 0, AvUnitTable.DistanceReading(scratch, 0, metres, units)).ToUpperInvariant();
            }
            gauge = new GaugeSpec
            {
                Id = "ace", Label = "ACE HUNT", Kind = GaugeKind.Needle, Angle = ((bearing - heading) % 360f + 360f) % 360f, Text = callsign, Sub = distance,
                Tone = metres <= CautionM ? ScopeTone.Amber : ScopeTone.Ink, Rank = 100,
            };
            return true;
        }
    }
}
