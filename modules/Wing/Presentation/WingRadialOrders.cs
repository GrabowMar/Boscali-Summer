using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>
    /// <see cref="IWingOrders"/> for the C-key menu: whole-wing orders through <see cref="WingCommands"/>, the same
    /// path the call ladder and WMC use, so acks, networking and refusals behave identically.
    /// </summary>
    internal sealed class WingRadialOrders : IWingOrders
    {
        public int Members => WingService.Instance != null ? WingService.Instance.Members.Count : 0;

        public string Shape
        {
            get
            {
                FormationSelection selection = WingService.Instance?.Selection;
                return selection?.Current?.Name?.ToUpperInvariant() ?? "";
            }
        }

        public void FormUp() => WingCommands.FormUp(WingScope.Wing);
        public void Engage() => WingCommands.Engage(WingScope.Wing);
        public void BreakOff() => WingCommands.Disengage(WingScope.Wing);
        public void ClearMySix() => WingCommands.ClearMySix();
        public void BogeyDope() => WingCommands.BogeyDope();
        public void EscortMe() => WingCommands.EscortMe();
        public void NextShape() => WingCommands.NextShape();
        public void ReturnToBase() => WingCommands.Rtb(WingScope.Wing);
    }
}
