using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace BoscaliSummer.Core.Storage
{
    /// <summary>
    /// Folders the player fills (radio music, event art) live under BepInEx/config/BoscaliSummer. Mod managers own
    /// BepInEx/plugins: NOMM moves a disabled mod's folder to disabledPlugins and deletes the older of two entries
    /// with the same name, so a folder the mod re-created there while the game ran became a second "BoscaliSummer"
    /// that NOMM kept while it deleted the real install. Nothing here ever creates a path under plugins.
    /// </summary>
    internal static class ModFolders
    {
        /// <summary>BepInEx/config/BoscaliSummer/<paramref name="name"/>, after moving over what an older version kept in plugins.</summary>
        public static string UserContent(string name, ManualLogSource log)
        {
            string target = Path.Combine(Paths.ConfigPath, "BoscaliSummer", name);
            string legacy = Path.Combine(Paths.PluginPath, "BoscaliSummer", name);
            try
            {
                if (!Directory.Exists(target) && Directory.Exists(legacy))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    Directory.Move(legacy, target);
                    log?.LogInfo("Moved " + name + " from the plugins folder to BepInEx/config/BoscaliSummer/" + name + ".");
                }
            }
            catch (Exception error)
            {
                log?.LogWarning("Could not move " + legacy + " to " + target + " (" + error.Message + "); move it by hand.");
            }
            return target;
        }
    }
}
