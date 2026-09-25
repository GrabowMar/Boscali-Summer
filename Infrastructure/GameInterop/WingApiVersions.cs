namespace BoscaliSummer.Runtime
{
    /// <summary>Wing Command public API versions Boscali can drive. Pure so the test runner links it.</summary>
    internal static class WingApiVersions
    {
        /// <summary>WingSquad 1 (0.9.x) and 2 (the 1.0 wing: fresh roster, same method names).</summary>
        internal static bool SupportsSquad(int version) => version == 1 || version == 2;
    }
}
