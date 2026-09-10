using System.IO;
using BepInEx.Logging;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal static class RadioStarterLayout
    {
        public static void Ensure(string root, ManualLogSource logger)
        {
            Directory.CreateDirectory(root);

            CopyResourceIfMissing(
                "BoscaliSummer.RadioAssets.stations-readme.txt",
                Path.Combine(root, "README.txt"), logger);
            for (int i = 0; i < BuiltInStationRules.ImportFolderNames.Length; i++)
                Directory.CreateDirectory(Path.Combine(
                    root, BuiltInStationRules.ImportFolderNames[i]));
        }

        private static void CopyResourceIfMissing(
            string resourceName, string target, ManualLogSource logger)
        {
            if (File.Exists(target)) return;
            using Stream source = typeof(RadioStarterLayout).Assembly.GetManifestResourceStream(resourceName);
            if (source == null)
            {
                logger?.LogWarning("Embedded radio starter asset missing: " + resourceName);
                return;
            }
            using FileStream destination = File.Create(target);
            source.CopyTo(destination);
        }
    }
}
