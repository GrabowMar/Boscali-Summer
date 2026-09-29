using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class BuildAvionicsUiBundle
{
    private const string Src = "Assets/NOA";
    private const string Out = "Assets/NOA/Baked";

    public static void Run()
    {
        try
        {
            EnsureTmpEssentials();
            Directory.CreateDirectory(Out);
            var charsets = ReadCharsets(Path.Combine(Src, "charsets.txt"));
            var icons = ReadIcons(Path.Combine(Src, "icons.txt"), Path.Combine(Src, "Fonts/tabler-icons.min.css"));
            var missing = new Dictionary<string, string>();
            var assets = new List<string>();

            assets.Add(Bake("BarlowCondensed-Medium", "NOA Barlow Condensed Medium SDF", charsets["cond"], 64, 1024, missing));
            assets.Add(Bake("BarlowCondensed-SemiBold", "NOA Barlow Condensed SemiBold SDF", charsets["cond"], 64, 1024, missing));
            assets.Add(Bake("JetBrainsMono-Regular", "NOA JetBrains Mono Regular SDF", charsets["mono"], 40, 1024, missing));
            assets.Add(Bake("JetBrainsMono-Bold", "NOA JetBrains Mono Bold SDF", charsets["mono"], 40, 1024, missing));
            string iconChars = new string(icons.Values.Select(c => (char)c).ToArray());
            assets.Add(Bake("tabler-icons", "NOA Icons SDF", iconChars, 64, 1024, missing));
            assets.Add(MakeNoise());
            foreach (string s in new[] { "NOA_UI_Fx", "NOA_UI_Glass", "NOA_UI_Blur" })
            {
                string p = Src + "/Shaders/" + s + ".shader";
                var sh = AssetDatabase.LoadAssetAtPath<Shader>(p);
                if (sh == null || ShaderUtil.ShaderHasError(sh)) throw new Exception("Shader failed: " + p);
                assets.Add(p);
            }

            Directory.CreateDirectory("BundleOutput");
            var build = new AssetBundleBuild { assetBundleName = "avionics-ui.bundle", assetNames = assets.ToArray() };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64) == null)
                throw new Exception("BuildAssetBundles returned null");

            File.WriteAllText("BundleOutput/avionics-ui.manifest.json", Manifest(icons, missing));
            File.WriteAllText("build_result.txt", "SUCCESS");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            File.WriteAllText("build_result.txt", "FAILED: " + ex);
            EditorApplication.Exit(1);
        }
    }

    private static void EnsureTmpEssentials()
    {
        if (Shader.Find("TextMeshPro/Distance Field") != null) return;
        string pkg = Path.GetFullPath("Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage");
        if (!File.Exists(pkg)) throw new Exception("TMP Essential Resources package not found: " + pkg);
        AssetDatabase.ImportPackage(pkg, false);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (Shader.Find("TextMeshPro/Distance Field") == null) throw new Exception("TMP SDF shader still missing after import");
    }

    private static string Bake(string file, string name, string chars, int pointSize, int atlas, Dictionary<string, string> missing)
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(Src + "/Fonts/" + file + ".ttf");
        if (font == null) throw new Exception("Font not imported: " + file);
        TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(font, pointSize, 6, GlyphRenderMode.SDFAA, atlas, atlas,
            AtlasPopulationMode.Dynamic, false);
        fa.name = name;
        fa.TryAddCharacters(chars, out string miss);
        missing[name] = string.Join(" ", (miss ?? "").Select(c => ((int)c).ToString("X4")));
        fa.atlasPopulationMode = AtlasPopulationMode.Static;
        string path = Out + "/" + name + ".asset";
        AssetDatabase.CreateAsset(fa, path);
        fa.atlasTextures[0].name = name + " Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = name + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        AssetDatabase.SaveAssets();
        return path;
    }

    private static string MakeNoise()
    {
        var tex = new Texture2D(64, 64, TextureFormat.R8, false, true) { name = "noise64", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var rng = new System.Random(1928);
        var px = new Color32[64 * 64];
        for (int i = 0; i < px.Length; i++) { byte b = (byte)rng.Next(256); px[i] = new Color32(b, b, b, 255); }
        tex.SetPixels32(px); tex.Apply(false, false);
        string path = Out + "/noise64.asset";
        AssetDatabase.CreateAsset(tex, path);
        return path;
    }

    private static Dictionary<string, string> ReadCharsets(string path)
    {
        var map = new Dictionary<string, string>();
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('='); var sb = new StringBuilder();
            foreach (string part in line.Substring(eq + 1).Split(','))
            {
                string[] r = part.Split('-');
                int a = Convert.ToInt32(r[0], 16), b = r.Length > 1 ? Convert.ToInt32(r[1], 16) : a;
                for (int c = a; c <= b; c++) sb.Append((char)c);
            }
            map[line.Substring(0, eq)] = sb.ToString();
        }
        return map;
    }

    private static SortedDictionary<string, int> ReadIcons(string listPath, string cssPath)
    {
        string css = File.ReadAllText(cssPath);
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string raw in File.ReadAllLines(listPath))
        {
            string name = raw.Trim(); if (name.Length == 0 || name[0] == '#') continue;
            Match m = Regex.Match(css, @"\.ti-" + Regex.Escape(name) + @":before\{content:""\\([0-9a-fA-F]{4,5})""");
            if (!m.Success) throw new Exception("Unknown Tabler icon: " + name);
            int cp = Convert.ToInt32(m.Groups[1].Value, 16);
            if (cp > 0xFFFF) throw new Exception("Icon outside the BMP (TMP char path): " + name);
            result[name] = cp;
        }
        return result;
    }

    private static string Manifest(SortedDictionary<string, int> icons, Dictionary<string, string> missing)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"unity\": \"").Append(Application.unityVersion).Append("\",\n  \"tmp\": \"3.0.6\",\n");
        sb.Append("  \"fonts\": [\"NOA Barlow Condensed Medium SDF\", \"NOA Barlow Condensed SemiBold SDF\", \"NOA JetBrains Mono Regular SDF\", \"NOA JetBrains Mono Bold SDF\", \"NOA Icons SDF\"],\n");
        sb.Append("  \"shaders\": [\"NOA/UI/Fx\", \"NOA/UI/Glass\", \"NOA/UI/Blur\"],\n  \"textures\": [\"noise64\"],\n  \"icons\": {");
        sb.Append(string.Join(",", icons.Select(p => "\n    \"" + p.Key + "\": \"" + p.Value.ToString("x4") + "\"")));
        sb.Append("\n  },\n  \"missing\": {");
        sb.Append(string.Join(",", missing.Select(p => "\n    \"" + p.Key + "\": \"" + p.Value + "\"")));
        sb.Append("\n  }\n}\n");
        return sb.ToString();
    }
}
