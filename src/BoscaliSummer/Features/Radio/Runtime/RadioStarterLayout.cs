using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal static class RadioStarterLayout
    {
        private sealed class Starter
        {
            public string Folder;
            public string Resource;
        }

        private static readonly Starter[] Starters =
        {
            new Starter { Folder = "Agrapol FM", Resource = "agrapol-fm.png" },
            new Starter { Folder = "Maris Network", Resource = "maris-network.png" },
            new Starter { Folder = "Base Broadcast", Resource = "base-broadcast.png" }
        };

        public static void Ensure(string root, ManualLogSource logger)
        {
            Directory.CreateDirectory(root);

            Assembly assembly = typeof(RadioStarterLayout).Assembly;
            CopyResourceIfMissing(
                assembly, "BoscaliSummer.RadioAssets.stations-readme.txt",
                Path.Combine(root, "README.txt"), logger);
            for (int i = 0; i < Starters.Length; i++)
            {
                string directory = Path.Combine(root, Starters[i].Folder);
                Directory.CreateDirectory(directory);
                string target = Path.Combine(directory, "station.png");
                string resourceName = "BoscaliSummer.RadioAssets." + Starters[i].Resource;
                CopyResourceIfMissing(assembly, resourceName, target, logger);
            }
        }

        private static void CopyResourceIfMissing(
            Assembly assembly, string resourceName, string target, ManualLogSource logger)
        {
            if (File.Exists(target)) return;
            using Stream source = assembly.GetManifestResourceStream(resourceName);
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
