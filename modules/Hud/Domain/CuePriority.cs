namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The TAC BOARD page registry, in the same order as the shared BoardPage contract.</summary>
    internal enum ScopePage : byte { Threat, Shot, MissileCam, Approach, Wing, Strike, Dock, Objective, Systems, Cargo }

    /// <summary>Page registry masks. Page selection is saved per slot in BoardOptions, independent of activity.</summary>
    internal static class CuePriority
    {
        public const int PageCount = 10;
        public static ushort Bit(ScopePage page) => (ushort)(1 << (int)page);
        public static bool Has(ushort mask, ScopePage page) => (mask & Bit(page)) != 0;
    }
}
