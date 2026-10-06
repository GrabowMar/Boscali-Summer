namespace BoscaliSummer.Modules.Wing.Domain
{
    internal static class AceIngress
    {
        internal static string Airframe(int tier) => tier == 1 ? "T/A-30" : tier == 2 ? "CT-7" :
            tier == 3 ? "FS-12" : tier == 4 ? "FS-20" : tier == 5 ? "KR-67" : null;

    }
}
