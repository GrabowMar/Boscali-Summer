// One-time Blueprinter project setup, scripted so it runs headless:
//   Unity -batchmode -quit -projectPath <bp> -executeMethod Vanguard.VanguardSetup.<Step>
// Each step is its own Unity launch so assembly imports get a domain reload in between.
// Env: NO_GAME_EXE, NO_GAME_VERSION, NO_RIP_ASSETS (AssetRipper ExportedProject/Assets).
using System;
using Blueprinter;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static class VanguardSetup
    {
        static string Env(string key) =>
            Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException("missing env " + key);

        public static void ImportAssemblies()
        {
            if (!GameAssemblies.TryGetManagedFolder(Env("NO_GAME_EXE"), out var managed))
                throw new InvalidOperationException("NuclearOption.exe not recognised");
            GameAssemblies.Import(managed, Env("NO_GAME_VERSION"));
            GameAssemblySync.Synchronize();
            Debug.Log("[VanguardSetup] assemblies imported: " + GameAssemblies.IsInstalled);
        }

        public static void ImportAssets()
        {
            var rip = Env("NO_RIP_ASSETS");
            if (!AssetRipperImporter.IsAssetsFolder(rip))
                throw new InvalidOperationException("not an AssetRipper Assets folder: " + rip);
            AssetRipperImporter.Import(rip);
            Debug.Log("[VanguardSetup] assets imported");
        }

        public static void RefreshOps()
        {
            OpReferenceIndex.Refresh();
            Debug.Log("[VanguardSetup] op references refreshed");
        }

        public static void BuildDoNotShip()
        {
            ModBuilder.BuildGameAssets();
            Debug.Log("[VanguardSetup] _donotship built");
        }
    }
}
