using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>The pages of one board, indexed by <see cref="ScopePage"/>. Every page is registered here already: a page group edits only its own file.</summary>
    internal static class ScopePages
    {
        public static ScopePageBase[] Create() => new ScopePageBase[CuePriority.PageCount]
        {
            new ThreatPage(), new ShotPage(), new MissileCamPage(), new ApproachPage(), new WingPage(),
            new StrikePage(), new DockPage(), new ObjectivePage(), new SystemsPage(), new CargoPage(),
        };
    }
}
