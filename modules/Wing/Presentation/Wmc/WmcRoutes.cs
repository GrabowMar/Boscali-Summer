using System.Collections.Generic;
using System.IO;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Saved routes on disk (spec WMC program §4): <c>routes.user.json</c> under the v1 data root, read once, written
    /// whole through a temp file.</summary>
    internal static class WmcRoutes
    {
        private static RouteStore store;

        private static string FilePath => Path.Combine(WingConfig.RecordsRoot, "routes.user.json");

        public static RouteStore Store
        {
            get
            {
                if (store != null) return store;
                var errors = new List<string>();
                string json = WmcUserFile.Read(FilePath, errors, out _);
                store = RouteStore.FromJson(json, errors);
                WmcUserFile.Report("[Routes]", FilePath, errors, keepAside: false);
                return store;
            }
        }

        public static void Save() => WmcUserFile.Write("[Routes]", FilePath, Store.ToJson());
    }
}
