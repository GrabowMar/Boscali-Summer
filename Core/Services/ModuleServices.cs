namespace BoscaliSummer.Core.Services
{
    /// <summary>
    /// Process-wide view of the module service registry so a later-installing module
    /// (Command) can still be discovered by an earlier one (Support) at MFD build time.
    /// </summary>
    internal static class ModuleServices
    {
        /// <summary>Main thread only. Set by BoscaliMod at startup, cleared at shutdown.</summary>
        internal static ServiceRegistry Active { get; set; }

        internal static bool TryGet<T>(out T service) where T : class
        {
            if (Active != null) return Active.TryGet(out service);
            service = null;
            return false;
        }
    }
}
