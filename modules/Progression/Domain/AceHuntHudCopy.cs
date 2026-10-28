
namespace BoscaliSummer.Modules.Progression.Domain
{
    /// <summary>
    /// The ace-hunt widget's copy, pure so the test project links it directly. It names the
    /// encounter the SQD panel is showing, never a second version of it.
    /// </summary>
    internal static class AceHuntHudCopy
    {
        /// <summary>"ACE HUNT · VULTURE", falling back to the wing when the ace is unnamed.</summary>
        public static string Text(string aceName, string wingName)
        {
            string name = !string.IsNullOrEmpty(aceName) ? aceName
                : !string.IsNullOrEmpty(wingName) ? wingName : "UNKNOWN";
            return "ACE HUNT · " + name;
        }
    }
}
