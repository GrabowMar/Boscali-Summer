namespace BoscaliSummer.Framework.Features
{
    /// <summary>
    /// Process-wide view of the feature service registry so a later-installing feature
    /// (Command) can still be discovered by an earlier one (Support) at MFD build time.
    /// </summary>
    internal static class ModServices
    {
        internal static ServiceRegistry Active { get; set; }

        internal static bool TryGet<T>(out T service) where T : class
        {
            if (Active != null) return Active.TryGet(out service);
            service = null;
            return false;
        }
    }
}
