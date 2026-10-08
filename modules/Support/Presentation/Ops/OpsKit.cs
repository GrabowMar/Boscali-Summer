using System;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation.Ops
{
    /// <summary>Small shared helpers of the OPS page and the front windows.</summary>
    internal static class OpsKit
    {
        /// <summary>The vanilla map grid square (what the map's own corner readout shows), else plain kilometres.</summary>
        public static string Grid(float x, float z)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && map.gridLabels != null)
            {
                try { return map.gridLabels.GetGridPosition(new GlobalPosition(x, 0f, z)).ToUpperInvariant(); }
                catch (Exception) { /* fall back to kilometres */ }
            }
            return TheaterGrid.Kilometres(x, z);
        }

        public static AvState Of(AnchorHealth h) => h == AnchorHealth.Live ? AvState.Ready : h == AnchorHealth.Damaged ? AvState.Caution : AvState.Danger;

        public static string Word(AnchorHealth h) => h == AnchorHealth.Live ? "LIVE" : h == AnchorHealth.Damaged ? "DAMAGED" : "DOWN";

        public static AvState Of(CallState s) =>
            s == CallState.Ready ? AvState.Ready : s == CallState.Armed ? AvState.Caution : s == CallState.Pending ? AvState.Info
            : s == CallState.Offline || s == CallState.LowCredit ? AvState.Danger : AvState.Inert;

        public static AvControl.Spec Spec(string label, Action click, AvButtonStyle style = AvButtonStyle.Default, AvIcon icon = AvIcon.None) =>
            new AvControl.Spec(label, click, style, icon);

        public static void Enable(AvControl c, bool on) { if (c != null && c.Interactable != on) c.Interactable = on; }
        public static void Label(AvControl c, string text) { if (c != null && c.Label != text) c.Label = text; }
        public static void Help(AvControl c, string text) { if (c != null && !string.IsNullOrEmpty(text) && c.Help != text) c.Help = text; }
    }
}
