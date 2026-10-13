using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>Lets the radio write into the map screen's tactical log without naming it (<see cref="ITacticalLog"/>).</summary>
    internal sealed class MfdRadioLog : ITacticalLog
    {
        public void Radio(string text) => MfdLogPanel.Radio(text);
    }
}
