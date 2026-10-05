#if UNITY_EDITOR
using System;
using System.IO;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class AvBundleUnityCheck
{
    public static void Run()
    {
        // TMP needs its essentials (settings + SDF shader) before any label lays out; the import is
        // asynchronous in batchmode, so re-enter once it completes (same pattern as the Support check).
        if (Shader.Find("TextMeshPro/Distance Field") == null)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            UnityEditor.AssetDatabase.importPackageCompleted += _ => UnityEditor.EditorApplication.delayCall += Run;
            UnityEditor.AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
            return;
        }
        string result = "OK";
        try
        {
            // Review focus 2: garbage bytes fail closed, no throw.
            AvBundle.ResetForTests();
            AvBundle.LoadFromBytes(new byte[] { 1, 2, 3, 4 }, Debug.Log);
            Require(!AvBundle.Available, "garbage bundle must not report Available");
            Require(AvBundle.Font("NOA Icons SDF") == null, "garbage bundle yields no font");

            // Real bytes.
            AvBundle.ResetForTests();
            byte[] bytes = File.ReadAllBytes("avionics-ui.bundle");
            AvBundle.LoadFromBytes(bytes, Debug.Log);
            Require(AvBundle.Available, "real bundle loads");

            // Review focus 3: a second load is a no-op, not a Unity 'already loaded' error.
            AvBundle.LoadFromBytes(bytes, Debug.Log);
            Require(AvBundle.Available, "second load keeps the bundle");

            // S1: every font present and renders a label with non-zero width.
            foreach (string f in new[] { "NOA Rajdhani Medium SDF", "NOA Rajdhani SemiBold SDF",
                         "NOA JetBrains Mono Regular SDF", "NOA JetBrains Mono Bold SDF", "NOA Icons SDF" })
                Require(AvBundle.Font(f) != null, "font resolves: " + f);
            var canvas = new GameObject("c", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var label = new GameObject("l", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(canvas.transform, false);
            label.font = AvBundle.Font("NOA Rajdhani SemiBold SDF");
            label.text = "SPACE / STATUS 01/03";
            label.ForceMeshUpdate();
            Require(label.textInfo.characterCount == 20 && label.preferredWidth > 50f, "bundled font lays out text");

            // S2: 50 graphics with the Fx material share one material instance -> one batch key.
            Shader fx = AvBundle.Shader("NOA/UI/Fx");
            Require(fx != null, "Fx shader resolves");
            var mat = new Material(fx);
            Material first = null;
            for (int i = 0; i < 50; i++)
            {
                var img = new GameObject("g" + i, typeof(RectTransform)).AddComponent<Image>();
                img.transform.SetParent(canvas.transform, false);
                img.material = mat;
                if (first == null) first = img.materialForRendering;
                Require(ReferenceEquals(img.materialForRendering, first), "fx graphics share one material (batchable)");
            }

            // S3: glass + blur + noise.
            Require(AvBundle.Shader("NOA/UI/Glass") != null, "Glass shader resolves");
            Require(AvBundle.Shader("NOA/UI/Blur") != null, "Blur shader resolves");
            Require(AvBundle.Noise != null && AvBundle.Noise.width == 64, "noise64 resolves");

            // Review focus 5: an unknown shader name is null, not an exception.
            Require(AvBundle.Shader("NOA/UI/Nope") == null, "unknown shader is null");
        }
        catch (Exception e) { result = "FAIL: " + e; }
        File.WriteAllText("result.txt", result);
        UnityEditor.EditorApplication.Exit(result == "OK" ? 0 : 1);
    }

    private static void Require(bool ok, string what) { if (!ok) throw new Exception(what); }
}
#endif
