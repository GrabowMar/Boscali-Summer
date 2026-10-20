using System;
using BoscaliSummer.Modules.Support.Domain;
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
            return FallbackGrid(x, z);
        }

        /// <summary>Answers when the map has no grid labels (kilometres). The offline renders swap in a letter-and-digit grid so fixture text is as short as the game's.</summary>
        internal static Func<float, float, string> FallbackGrid = (x, z) => TheaterGrid.Kilometres(x, z);

        public static AvControl.Spec Spec(string label, Action click, AvButtonStyle style = AvButtonStyle.Default, AvIcon icon = AvIcon.None) =>
            new AvControl.Spec(label, click, style, icon);

        public static void Enable(AvControl c, bool on) { if (c != null && c.Interactable != on) c.Interactable = on; }
        public static void Label(AvControl c, string text) { if (c != null && c.Label != text) c.Label = text; }
    }
}
