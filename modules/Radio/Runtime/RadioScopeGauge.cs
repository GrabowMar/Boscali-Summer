using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>
    /// The radio's gauge of the TAC BOARD's row (HUD v8): signal bars and the station, shown only while a station is really talking (the receiver on, not paused,
    /// tuned to a station that is on the air and past the squelch). The bars are the link budget's own quality (<c>RadioReception.Quality</c>, full scale for a local
    /// archive with no tower to model), the word under them the receiver's S report. Client-local: the receiver is the pilot's own.
    /// </summary>
    internal sealed class RadioScopeGauge
    {
        private readonly RadioManager manager;
        private readonly string[] reports = new string[12];

        public RadioScopeGauge(RadioManager manager) { this.manager = manager; }

        public void Register() => ScopeGauges.Register("radio", Read);

        public bool Read(out GaugeSpec gauge)
        {
            gauge = default;
            RadioManager m = manager;
            if (m == null || !m.HasChannels || !m.IsEngaged || m.IsPaused || m.IsOffAir || m.IsOffStation || m.IsScanning) return false;
            RadioReception reception = m.Reception;
            bool modelled = m.ReceptionModelled;
            if (modelled && !reception.Open(m.Squelch)) return false;
            float quality = modelled ? Mathf.Clamp01(reception.Quality) : 1f;
            int s = Mathf.Clamp(Mathf.RoundToInt(quality * 9f), 0, reports.Length - 1);
            gauge = new GaugeSpec
            {
                Id = "radio", Label = "RADIO", Kind = GaugeKind.Bars, Value = quality, Text = m.CurrentChannelName, Sub = modelled ? reports[s] ?? (reports[s] = "S" + s) : "LOCAL",
                Tone = ScopeTone.Ink, Rank = 50,
            };
            return true;
        }
    }
}
