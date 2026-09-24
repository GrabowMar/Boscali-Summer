using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// Reserved presentation seam for the later plane HUD overhaul. Superevent state is
    /// already replicated by EventsManager; this branch deliberately renders nothing.
    /// </summary>
    internal static class SuperEventPlaneHud
    {
        public static void OnSuperEvent(ActiveEventView view)
        {
            // HUD overhaul will own placement, cockpit visibility and interaction.
        }
    }
}
