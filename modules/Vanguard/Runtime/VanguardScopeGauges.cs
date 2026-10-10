using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Vanguard's gauges of the TAC BOARD's row (HUD v8), each answering from state the module already holds and showing only while it has something to say:
    /// LANCE (the RG-12 capacitor: charge while the trigger is held, recharge after a shot), AEGIS (darts left) and REMORA (drones flying, on STRIKE or SCREEN). The LANCE and AEGIS
    /// figures are the local aircraft's own; the REMORA count is the server's registry, so it shows on the host and in solo only. SKYWELL's gauge is <see cref="Presentation.SkywellScopeFeed"/>'s.
    /// The LANCE gauge stays after the corner takeover: the RG-12 keeps its own charge bank and never draws the aircraft capacitor, so the stores header's capacitor row cannot show it.
    /// </summary>
    internal sealed class VanguardScopeGauges
    {
        private readonly IDroneCommand drones;
        private readonly string[] percents = new string[101], lanceRounds = new string[100], dartRounds = new string[100];
        private Aircraft stationsFor;
        private WeaponStation lance, aegis;
        private int droneKey = -1;
        private string droneSub;

        public VanguardScopeGauges(IDroneCommand drones) { this.drones = drones; }

        public void Register()
        {
            ScopeGauges.Register("lance", ReadLance);
            ScopeGauges.Register("aegis", ReadAegis);
            ScopeGauges.Register("remora", ReadRemora);
        }

        private static bool Local(out Aircraft me) => GameManager.GetLocalAircraft(out me) && me != null && !me.disabled;

        /// <summary>The pod stations of the local aircraft, found again only when the aircraft changes.</summary>
        private void Stations(Aircraft me)
        {
            if (stationsFor == me) return;
            stationsFor = me;
            lance = aegis = null;
            for (int i = 0; me.weaponStations != null && i < me.weaponStations.Count; i++)
            {
                WeaponStation s = me.weaponStations[i];
                if (s?.WeaponInfo == null) continue;
                if (lance == null && s.WeaponInfo.name.Contains(VanguardKeys.LanceInfo)) lance = s;
                if (aegis == null && s.WeaponInfo.name.Contains(VanguardKeys.AegisInfo)) aegis = s;
            }
        }

        private string Percent(float v)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * 100f), 0, 100);
            return percents[n] ?? (percents[n] = n + "%");
        }

        private static string Rounds(int n, string[] cache, string tail)
        {
            n = Mathf.Clamp(n, 0, cache.Length - 1);
            return cache[n] ?? (cache[n] = n + tail);
        }

        /// <summary>LANCE: the capacitor as an arc, the pod's rounds under it; empty reads EMPTY in amber.</summary>
        public bool ReadLance(out GaugeSpec gauge)
        {
            gauge = default;
            if (!Local(out Aircraft me)) return false;
            Stations(me);
            if (lance == null) return false;
            LanceService.TryState(me, out float value, out bool charging, out bool recharging);
            bool empty = lance.Ammo <= 0;
            gauge = new GaugeSpec
            {
                Id = "lance", Label = "LANCE", Kind = GaugeKind.Arc, Value = empty ? 0f : Mathf.Clamp01(value), Text = empty ? "EMPTY" : charging && value >= 1f ? "FIRE" : Percent(value),
                Sub = Rounds(lance.Ammo, lanceRounds, " RDS"), Tone = empty ? ScopeTone.Amber : recharging ? ScopeTone.Dim : ScopeTone.Ink, Ready = !empty && charging && value >= 1f, Rank = 60,
            };
            return true;
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
