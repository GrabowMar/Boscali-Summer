using System.Collections.Generic;
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
        private readonly List<FormationDefinition> shapes = new List<FormationDefinition>(24);

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
        public void ReturnToBase() => WingCommands.Rtb(WingScope.Wing);

        private List<FormationDefinition> Shapes()
        {
            shapes.Clear();
            WingService.Instance?.Selection?.Suitable(shapes);
            return shapes;
        }

        public int ShapeCount => Shapes().Count;

        public string ShapeLabel(int index) => index >= 0 && index < Shapes().Count ? shapes[index].Name.ToUpperInvariant() : "";

        public string ShapeFamily(int index) => index >= 0 && index < Shapes().Count ? shapes[index].Family.ToUpperInvariant() : "";

        public bool ShapeActive(int index) => index >= 0 && index < Shapes().Count && shapes[index] == WingService.Instance.Selection.Current;

        public void SelectShape(int index)
        {
            if (index >= 0 && index < Shapes().Count) WingOrders.Run(new WingOrder { Kind = OrderKind.SetShape, Text = shapes[index].Id });
        }
    }
}
