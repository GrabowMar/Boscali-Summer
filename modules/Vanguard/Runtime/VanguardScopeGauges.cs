using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Vanguard's gauges of the TAC BOARD's row (HUD v8), each answering from state the module already holds and showing only while it has something to say:
    /// AEGIS (darts left) and REMORA (drones flying, on STRIKE or SCREEN). The AEGIS figure is the local aircraft's own; the REMORA count is the server's registry, so it shows on the host and in solo only.
    /// SKYWELL's gauge is <see cref="Presentation.SkywellScopeFeed"/>'s. The RG-12 LANCE has no gauge since the corner takeover: the stores header owns the weapon's rounds and the capacitor row, and
    /// the LANCE's own charge bank does not draw on the aircraft's capacitor, so there is no real number to put on that row.
    /// </summary>
    internal sealed class VanguardScopeGauges
    {
        private readonly IDroneCommand drones;
        private readonly string[] dartRounds = new string[100];
        private Aircraft stationsFor;
        private WeaponStation aegis;
        private int droneKey = -1;
        private string droneSub;

        public VanguardScopeGauges(IDroneCommand drones) { this.drones = drones; }

        public void Register()
        {
            ScopeGauges.Register("aegis", ReadAegis);
            ScopeGauges.Register("remora", ReadRemora);
        }

        private static bool Local(out Aircraft me) => GameManager.GetLocalAircraft(out me) && me != null && !me.disabled;

        /// <summary>The pod stations of the local aircraft, found again only when the aircraft changes.</summary>
        private void Stations(Aircraft me)
        {
            if (stationsFor == me) return;
            stationsFor = me;
            aegis = null;
            for (int i = 0; me.weaponStations != null && i < me.weaponStations.Count; i++)
            {
                WeaponStation s = me.weaponStations[i];
                if (s?.WeaponInfo == null) continue;
                if (aegis == null && s.WeaponInfo.name.Contains(VanguardKeys.AegisInfo)) aegis = s;
            }
        }

        private static string Rounds(int n, string[] cache, string tail)
        {
            n = Mathf.Clamp(n, 0, cache.Length - 1);
            return cache[n] ?? (cache[n] = n + tail);
        }

        /// <summary>AEGIS: the darts left of the pod, as an arc of what it was loaded with.</summary>
        public bool ReadAegis(out GaugeSpec gauge)
        {
            gauge = default;
            if (!Local(out Aircraft me)) return false;
            Stations(me);
            if (aegis == null) return false;
            int left = aegis.Ammo;
            gauge = new GaugeSpec
            {
                Id = "aegis", Label = "AEGIS", Kind = GaugeKind.Arc, Value = aegis.FullAmmo > 0 ? Mathf.Clamp01((float)left / aegis.FullAmmo) : 0f, Text = left <= 0 ? "EMPTY" : Rounds(left, dartRounds, ""), Sub = "DARTS",
                Tone = left <= 0 ? ScopeTone.Amber : ScopeTone.Ink, Rank = 48,
            };
            return true;
        }

        /// <summary>REMORA: how many drones are flying and whether they are sent to STRIKE or kept on SCREEN. None flying: no gauge.</summary>
        public bool ReadRemora(out GaugeSpec gauge)
        {
            gauge = default;
            int ready = drones != null ? drones.Ready : 0;
            if (ready <= 0) return false;
            if (ready != droneKey) { droneKey = ready; droneSub = ready + " UP"; }
            gauge = new GaugeSpec { Id = "remora", Label = "REMORA", Kind = GaugeKind.Mode, Text = drones.Striking ? "STRIKE" : "SCREEN", Sub = droneSub, Value = 0f, Tone = ScopeTone.Ink, Rank = 48 };
            return true;
        }
    }
}
