// Builds the mod's one Blueprinter bundle ("Boscali Summer assets"): every content builder writes its
// assets under Assets/Blueprinter/Mods/BoscaliSummer/<Part>/, then a single ModBuilder.Build packs the folder.
// Run through tools/Compile-BlueprinterAssets.ps1, which sets the builders' environment variables.
// Env here: BS_ASSETS_OUT (the .nobp the csproj embeds), BS_ASSETS_VERSION.
using System;
using System.IO;
using System.Linq;
using Blueprinter;
using UnityEditor;
using UnityEngine;

public static class ModAssetsBuilder
{
    const string Folder = "BoscaliSummer";
    const string DisplayName = "Boscali Summer assets";

    public static void Run()
    {
        try
        {
            string output = Env("BS_ASSETS_OUT"), version = Env("BS_ASSETS_VERSION");
            Vanguard.VanguardBuilder.Run();
            Airborne.AirborneBuilder.Run();
            CargoShipBuilder.Run();
            AssetDatabase.SaveAssets();
            OpReferenceIndex.Refresh();

            string delivery = Path.GetFullPath("Delivery~");
            Directory.CreateDirectory(delivery);
            var started = DateTime.UtcNow;
            ModBuilder.Build(Folder, DisplayName, version, delivery);
            string built = Directory.GetFiles(delivery, "*.nobp")
                .FirstOrDefault(f => File.GetLastWriteTimeUtc(f) >= started.AddSeconds(-1))
                ?? throw new Exception("fresh bundle was not produced");
            Check(built);
            File.Copy(built, output, true);
            Debug.Log("[ModAssets] BUNDLE_OK " + Path.GetFileName(built) + " " + new FileInfo(built).Length + " bytes");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static string Env(string key) => Environment.GetEnvironmentVariable(key) ?? throw new Exception(key + " not set");

    // Reopens the shipped file: one manifest under our name, and no prefab lost a script on the way in.
    static void Check(string path)
    {
        var bundle = AssetBundle.LoadFromFile(path) ?? throw new Exception("cannot reopen " + path);
        try
        {
            var manifest = bundle.LoadAsset<TextAsset>("Assets/Blueprinter/Generated/patch_manifest.json");
            if (!manifest || !manifest.text.Contains("\"" + DisplayName + "\"")) throw new Exception("manifest is not " + DisplayName);
            var prefabs = bundle.LoadAllAssets<GameObject>();
            foreach (var prefab in prefabs)
                if (prefab.GetComponentsInChildren<Component>(true).Any(c => !c)) throw new Exception("missing script in " + prefab.name);
            foreach (string part in new[] { "Vanguard", "Airborne", "CargoShip" })
                if (!bundle.GetAllAssetNames().Any(n => n.Contains("/mods/" + Folder.ToLowerInvariant() + "/" + part.ToLowerInvariant() + "/")))
                    throw new Exception("bundle has no " + part + " assets");
            Debug.Log("[ModAssets] CHECK_OK prefabs=" + prefabs.Length + " assets=" + bundle.GetAllAssetNames().Length);
        }
        finally { bundle.Unload(true); }
    }
}
