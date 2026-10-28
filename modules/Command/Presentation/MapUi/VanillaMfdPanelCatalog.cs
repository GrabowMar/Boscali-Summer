
namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Stable identities for the stock map MFD surfaces Wing Command replaces.
    ///
    /// The game mutates the faction screens' short names during mission setup, so runtime
    /// binding prefers their controller component. This small pure catalog is the fallback
    /// and the common vocabulary for diagnostics, tests, and the owned panel headers.
    /// </summary>
    internal enum VanillaMfdPanelId
    {
        Unknown,
        Bdf,
        Map,
        Hud,
        Pala,
        Tgt,
        Mis,
    }

    internal static class VanillaMfdPanelCatalog
    {

        public static string Label(VanillaMfdPanelId id)
        {
            switch (id)
            {
                case VanillaMfdPanelId.Bdf: return "FAC";
                case VanillaMfdPanelId.Map: return "MAP";
                case VanillaMfdPanelId.Hud: return "HUD";
                case VanillaMfdPanelId.Pala: return "PALA";
                case VanillaMfdPanelId.Tgt: return "TGT";
                case VanillaMfdPanelId.Mis: return "MIS";
                default: return "MFD";
            }
        }
    }
}
