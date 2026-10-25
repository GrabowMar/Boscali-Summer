#if UNITY_EDITOR
namespace BoscaliSummer.Core.Fx
{
    // Native renderer cleanup is tested separately against the production DLL and installed URP.
    internal static class RenderCacheMaintenance
    {
        public static int Calls;
        public static string Error;
        public static int ClearUnusedStacks(out string error) { Calls++; error = Error; return 1; }
    }
}
#endif
