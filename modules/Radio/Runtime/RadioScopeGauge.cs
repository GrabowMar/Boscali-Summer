using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>Playback status only. Military traffic is presented on the COMMS strip.</summary>
    internal sealed class RadioScopeGauge
    {
        private readonly RadioManager manager;
        public RadioScopeGauge(RadioManager manager) { this.manager = manager; }
        public void Register() => ScopeGauges.Register("radio", Read);
        public bool Read(out GaugeSpec gauge)
        {
            gauge = default;
            if (manager == null || !manager.IsEngaged || manager.IsPaused) return false;
            gauge = new GaugeSpec { Id = "radio", Label = "RADIO", Kind = GaugeKind.Bars, Value = 1f,
                Text = manager.CurrentChannelName, Sub = manager.IsTalking ? "BROADCAST" : "MUSIC", Tone = ScopeTone.Ink, Rank = 50 };
            return true;
        }
    }
}
