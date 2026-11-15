param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '../modules/Spooktober/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/Spooktober/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
# Compiles Spooktober shaders, with rendered portal and opaque-ground depth probes.
# Plain CG, no pipeline includes; same recipe as Compile-NightVisionAssets.ps1.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliSpookBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Spook", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}'
foreach ($name in @('RadGhost.shader', 'Hellfire.shader', 'HellPortal.shader', 'HellMatter.shader', 'HellSky.shader', 'SootedWasteland.shader', 'HellInfection.shader')) {
    $source = if ($name -eq 'SootedWasteland.shader') { Join-Path $PSScriptRoot '../Core/Fx/Assets/Source/SootedWasteland.shader' } else { Join-Path $SourceDir $name }
    if (-not (Test-Path -LiteralPath $source)) { throw "Required shader source missing: $source" }
    Copy-Item -LiteralPath $source -Destination "$fixture/Assets/Spook/$name" -Force
    if ($name -eq 'SootedWasteland.shader') {
        # The existing wasteland is a decal. Hell also uses the exact same fragment on
        # a physical arena floor and extruded strata, which must occlude distant clouds.
        # Derive only this bundle's copy; Core's decal and its other consumers stay unchanged.
        $ashSource = Get-Content -LiteralPath $source -Raw
        if ($ashSource -notmatch 'ZWrite Off') { throw 'Wasteland render state changed; review Hell ground depth alias' }
        $ashSource = $ashSource -replace 'ZWrite Off', 'ZWrite On'
        # URP may populate _CameraDepthTexture with a prepass rather than a depth-buffer copy.
        # The cloud march reads that texture with ZTest Always, so writing only opaque depth is insufficient.
        $ashDepthPass = @'
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Off
            CGPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "UnityCG.cginc"
            float4 vertDepth(appdata_base v):SV_POSITION { return UnityObjectToClipPos(v.vertex); }
            float4 fragDepth():SV_Target { return 0; }
            ENDCG
        }
'@
        if (-not $ashSource.Contains('        Pass')) { throw 'Wasteland pass layout changed; review Hell depth alias' }
        $ashSource = $ashSource.Replace('        Pass', $ashDepthPass + "`n        Pass")
        Set-Content -LiteralPath "$fixture/Assets/Spook/$name" -Value $ashSource
    }
}
Set-Content -LiteralPath "$fixture/Assets/Spook/GrowthClockProbe.shader" -Value @'
Shader "Hidden/HellGrowthClockProbe"
{
    SubShader { Pass { ZWrite Off ZTest Always Cull Off
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        float4 frag(v2f_img i):SV_Target { return float4(_Time.y,0,0,1); }
        ENDCG
    } }
}
'@
Set-Content -LiteralPath "$fixture/Assets/Editor/BuildSpookBundle.cs" -Value @'
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
public static class BuildSpookBundle
{
    public static void Run()
    {
        try
        {
            string[] paths = { "Assets/Spook/RadGhost.shader", "Assets/Spook/Hellfire.shader", "Assets/Spook/HellPortal.shader", "Assets/Spook/HellMatter.shader", "Assets/Spook/HellSky.shader", "Assets/Spook/SootedWasteland.shader", "Assets/Spook/HellInfection.shader" };
            string[] names = { "Boscali/RadGhost", "Boscali/Hellfire", "Boscali/HellPortal", "Boscali/HellMatter", "Boscali/HellSky", "Boscali/SootedWasteland", "Boscali/HellInfection" };
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < paths.Length; i++)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(paths[i]);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    string detail = "";
                    if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        detail += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                    throw new Exception("Shader import failed: " + paths[i] + detail);
                }
                if (shader.name != names[i]) throw new Exception("Unexpected shader name: " + shader.name);
                if (!shader.isSupported) throw new Exception(names[i] + " is not supported on the build machine");
                if (i >= 3 && i <= 5)
                {
                    var depthProbe = new Material(shader);
                    int depthPass = depthProbe.FindPass("DepthOnly");
                    UnityEngine.Object.DestroyImmediate(depthProbe);
                    if (depthPass < 0 || shader.FindPassTagValue(depthPass, new ShaderTagId("LightMode")).name != "DepthOnly")
                        throw new Exception(names[i] + " is missing its URP DepthOnly prepass");
                }
            }
            // The runtime sets these per target (RadGhostLook.Profile); a rename here would silently do nothing.
            var probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>(paths[0]));
            foreach (string property in new[] { "_Intensity", "_BodyAlpha", "_RimPower", "_NoiseScale", "_ScrollSpeed",
                "_ScanDensity", "_ScanStrength", "_ShimmerScale", "_FarDistance", "_Strength" })
                if (!probe.HasProperty(property)) throw new Exception("RadGhost property missing: " + property);
            if (probe.renderQueue != 3010) throw new Exception("RadGhost queue is " + probe.renderQueue + ", expected Transparent+10");
            UnityEngine.Object.DestroyImmediate(probe);
            probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>(paths[1]));
            foreach (string property in new[] { "_Intensity", "_NoiseScale", "_FarDistance", "_Strength" })
                if (!probe.HasProperty(property)) throw new Exception("Hellfire property missing: " + property);
            UnityEngine.Object.DestroyImmediate(probe);
            probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>(paths[2]));
            if (!probe.HasProperty("_Strength")) throw new Exception("HellPortal property missing: _Strength");
            RenderPortal(probe);
            UnityEngine.Object.DestroyImmediate(probe);
            RenderAshDepth(AssetDatabase.LoadAssetAtPath<Shader>(paths[5]));
            RenderInfection(AssetDatabase.LoadAssetAtPath<Shader>(paths[6]));
            var build = new AssetBundleBuild { assetBundleName = "spooktober.bundle", assetNames = paths };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle compiler returned null");
            foreach (string path in paths)
                if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(path))) throw new Exception("Shader compile failed: " + path);
            if (new FileInfo("BundleOutput/spooktober.bundle").Length > 512 * 1024)
                throw new Exception("spooktober.bundle exceeds its 512 KiB loader limit");
            File.WriteAllText("build-result.txt", "PASS spooktober.bundle: " + string.Join(", ", names) + "; portal finite/black/transparent; ash occludes distant transparent cloud; three tagged DepthOnly passes; infection age0 clear, midgrowth partial, final frozen, edges feathered, finite non-HDR");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("build-result.txt", "FAIL " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void RenderPortal(Material material)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.GetComponent<Renderer>().sharedMaterial = material;
        var cameraObject = new GameObject("PortalProbeCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -2f);
        camera.orthographic = true;
        camera.orthographicSize = .6f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.25f, .4f, .5f, 1f);
        camera.allowHDR = true;
        var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        camera.targetTexture = target;
        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var read = new Texture2D(512, 512, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        read.Apply();
        Color center = read.GetPixel(256, 256), outside = read.GetPixel(0, 0);
        float redPeak = 0f;
        int crimsonPixels = 0;
        foreach (Color c in read.GetPixels())
        {
            if (float.IsNaN(c.r) || float.IsInfinity(c.r) || float.IsNaN(c.g) || float.IsInfinity(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.b))
                throw new Exception("Portal produced non-finite color");
            // Only the opaque dark rift is measured; the blue control background is deliberately brighter.
            if (c.b < .025f && c.g < .025f)
            {
                redPeak = Mathf.Max(redPeak, c.r);
                if (c.r > c.g * 2f + .003f) crimsonPixels++;
            }
        }
        if (center.r > .015f || center.g > .015f || center.b > .015f || redPeak > .2f || crimsonPixels == 0 ||
            Mathf.Abs(outside.r - .25f) > .002f || Mathf.Abs(outside.g - .4f) > .002f || Mathf.Abs(outside.b - .5f) > .002f)
            throw new Exception("Invalid restrained rift: center=" + center + "; red peak=" + redPeak + "; crimson pixels=" + crimsonPixels + "; outside=" + outside);
        File.WriteAllBytes("portal-probe.png", read.EncodeToPNG());
        material.SetFloat("_Strength", 0f);
        camera.Render();
        read.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        read.Apply();
        Color closed = read.GetPixel(256, 256);
        if (Mathf.Abs(closed.r - .25f) > .002f || Mathf.Abs(closed.g - .4f) > .002f || Mathf.Abs(closed.b - .5f) > .002f)
            throw new Exception("Closed rift did not restore the scene: " + closed);
        material.SetFloat("_Strength", 1f);
        RenderTexture.active = old;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(cameraObject);
        UnityEngine.Object.DestroyImmediate(quad);
        UnityEngine.Object.DestroyImmediate(read);
        UnityEngine.Object.DestroyImmediate(target);
    }

    private static void RenderAshDepth(Shader shader)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "Opaque ash floor depth probe";
        ground.transform.position = new Vector3(0f, 0f, -.1f);
        var ash = new Material(shader);
        ground.GetComponent<Renderer>().sharedMaterial = ash;
        var cloud = GameObject.CreatePrimitive(PrimitiveType.Quad);
        cloud.name = "Distant transparent cloud control";
        cloud.transform.position = new Vector3(0f, 0f, .1f);
        Shader transparent = Shader.Find("Unlit/Transparent");
        if (transparent == null) throw new Exception("Transparent depth-control shader unavailable");
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        var cloudMaterial = new Material(transparent) { mainTexture = texture, renderQueue = 2997 };
        cloud.GetComponent<Renderer>().sharedMaterial = cloudMaterial;
        var cameraObject = new GameObject("AshDepthProbeCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -2f);
        camera.orthographic = true;
        camera.orthographicSize = .6f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.25f, .4f, .5f, 1f);
        var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
        camera.targetTexture = target;
        RenderTexture previous = RenderTexture.active;
        camera.Render();
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); read.Apply();
        Color occluded = read.GetPixel(128, 128);
        if (occluded.r > .2f || occluded.g > .2f || occluded.b > .2f)
            throw new Exception("Opaque ash did not occlude the distant transparent cloud: " + occluded);
        File.WriteAllBytes("ash-depth-probe.png", read.EncodeToPNG());
        ground.SetActive(false);
        camera.Render();
        read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); read.Apply();
        Color visible = read.GetPixel(128, 128);
        if (visible.r < .8f || visible.g < .8f || visible.b < .8f)
            throw new Exception("Transparent cloud positive control was not visible: " + visible);
        File.WriteAllBytes("ash-depth-control.png", read.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(cameraObject);
        UnityEngine.Object.DestroyImmediate(ground);
        UnityEngine.Object.DestroyImmediate(cloud);
        UnityEngine.Object.DestroyImmediate(ash);
        UnityEngine.Object.DestroyImmediate(cloudMaterial);
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(read);
        UnityEngine.Object.DestroyImmediate(target);
    }

    private static void RenderInfection(Shader shader)
    {
        const int grid = 48, size = 512;
        var vertices = new Vector3[(grid + 1) * (grid + 1)];
        var world = new Vector2[vertices.Length];
        var arrivalCoverage = new Vector2[vertices.Length];
        var triangles = new int[grid * grid * 6];
        Vector2[] from = { new Vector2(-.35f,-.3f), new Vector2(0,.05f), new Vector2(0,.05f), new Vector2(0,.05f) };
        Vector2[] to = { new Vector2(0,.05f), new Vector2(.3f,.34f), new Vector2(-.3f,.35f), new Vector2(.35f,-.1f) };
        float[] starts = { 0, 14, 14, 14 }, ends = { 14, 36, 38, 30 };
        for (int y = 0; y <= grid; y++)
            for (int x = 0; x <= grid; x++)
            {
                int index = y * (grid + 1) + x;
                var p = new Vector2(x / (float)grid - .5f, y / (float)grid - .5f);
                vertices[index] = new Vector3(p.x, p.y, 0f);
                world[index] = new Vector2(12345f + p.x * 400f, -5432f + p.y * 400f);
                float nearest = float.MaxValue, arrival = 42f;
                for (int branch = 0; branch < from.Length; branch++)
                {
                    Vector2 span = to[branch] - from[branch];
                    float along = Mathf.Clamp01(Vector2.Dot(p - from[branch], span) / span.sqrMagnitude);
                    float distance = Vector2.Distance(p, from[branch] + span * along);
                    if (distance < nearest) { nearest = distance; arrival = Mathf.Lerp(starts[branch], ends[branch], along); }
                }
                arrivalCoverage[index] = new Vector2(arrival, Mathf.Clamp01((.07f - nearest) / .035f));
                if (x == grid || y == grid) continue;
                int t = (y * grid + x) * 6;
                triangles[t] = index; triangles[t + 1] = index + 1; triangles[t + 2] = index + grid + 1;
                triangles[t + 3] = index + 1; triangles[t + 4] = index + grid + 2; triangles[t + 5] = index + grid + 1;
            }
        var mesh = new Mesh { name = "Deterministic branched infection probe" };
        mesh.vertices = vertices; mesh.uv = world; mesh.uv2 = arrivalCoverage; mesh.triangles = triangles;
        mesh.RecalculateBounds();
        var ground = new GameObject("Infection time and feather probe", typeof(MeshFilter), typeof(MeshRenderer));
        ground.GetComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(shader);
        if (!material.HasProperty("_GrowthStart") || !material.HasProperty("_GrowthDuration") || material.renderQueue != 2980 || material.FindPass("DepthOnly") >= 0)
            throw new Exception("Infection contract changed: clock properties, transparent-20 queue, no opaque depth pass required");
        ground.GetComponent<MeshRenderer>().sharedMaterial = material;
        material.SetFloat("_GrowthDuration", 40f);
        var cameraObject = new GameObject("InfectionProbeCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -2);
        camera.orthographic = true; camera.orthographicSize = .6f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        Color background = new Color(.32f, .30f, .24f, 1f);
        camera.backgroundColor = background;
        var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
        camera.targetTexture = target;
        Shader clockShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spook/GrowthClockProbe.shader");
        if (clockShader == null || ShaderUtil.ShaderHasError(clockShader)) throw new Exception("Growth clock probe did not compile");
        var clockMaterial = new Material(clockShader);
        var clockTarget = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var clockRead = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var sheet = new Texture2D(size * 2, size * 2, TextureFormat.RGBAFloat, false, true);
        RenderTexture previous = RenderTexture.active;
        Color[] frozen = null;
        int[] covered = new int[4];
        int[] ages = { 0, 20, 40, 80 };
        int feathered = 0;
        for (int stage = 0; stage < ages.Length; stage++)
        {
            // Read the actual shader clock instead of adding a test-only time override to the shipped shader.
            Graphics.Blit(null, clockTarget, clockMaterial);
            RenderTexture.active = clockTarget;
            clockRead.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); clockRead.Apply();
            float now = clockRead.GetPixel(0, 0).r;
            material.SetFloat("_GrowthStart", now - ages[stage] + (stage == 0 ? 1f : 0f));
            camera.Render();
            RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0); read.Apply();
            Color[] pixels = read.GetPixels();
            if (Mathf.Abs(pixels[0].r - background.r) > .002f || Mathf.Abs(pixels[0].g - background.g) > .002f || Mathf.Abs(pixels[0].b - background.b) > .002f)
                throw new Exception("Infection changed untouched background");
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                if (!float.IsFinite(c.r) || !float.IsFinite(c.g) || !float.IsFinite(c.b) || c.maxColorComponent > 1.001f ||
                    c.r < -.001f || c.g < -.001f || c.b < -.001f || c.r > .322f || c.g > .302f || c.b > .242f)
                    throw new Exception("Infection produced non-finite/HDR/emissive colour at age " + ages[stage] + ": " + c);
                if (background.r - c.r > .01f) covered[stage]++;
                if (stage == 2 && c.r > .06f && c.r < .29f) feathered++;
                if (stage == 3 && !c.Equals(frozen[i])) throw new Exception("Infection continued changing after growth stopped");
            }
            if (stage == 2) frozen = pixels;
            sheet.SetPixels((stage % 2) * size, (1 - stage / 2) * size, size, size, pixels);
            File.WriteAllBytes("infection-age-" + ages[stage] + ".png", read.EncodeToPNG());
        }
        if (covered[0] != 0 || covered[1] <= 100 || covered[2] <= covered[1] || covered[3] != covered[2] || feathered < 16)
            throw new Exception("Invalid infection progression/feather: areas=" + string.Join(",", covered) + "; feather pixels=" + feathered);
        sheet.Apply();
        File.WriteAllBytes("infection-growth-contact.png", sheet.EncodeToPNG());
        File.WriteAllText("infection-probe.txt", "PASS age0/20/40/80 covered pixels=" + string.Join(",", covered) +
            "; feathered=" + feathered + "; final pixels exactly frozen; UV0 global metres, UV2 arrival/coverage; no HDR; original backdrop outside trails");
        RenderTexture.active = previous;
        camera.targetTexture = null;
        foreach (UnityEngine.Object item in new UnityEngine.Object[] { ground, cameraObject, mesh, material, clockMaterial, target, read, clockTarget, clockRead, sheet })
            UnityEngine.Object.DestroyImmediate(item);
    }
}
'@
Write-Output "Spooktober shader compiler: $fixture"
$resultPath = Join-Path $fixture 'build-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'BuildSpookBundle.Run', '-logFile', ('"' + "$fixture/build.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(10)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Spooktober shader compiler timed out: $fixture/build.log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    throw "Spooktober shader compilation failed: $fixture/build.log"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$target = Join-Path $OutputDir 'spooktober.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/spooktober.bundle" -Destination $target -Force
Get-Content -LiteralPath $resultPath
Write-Output "Bundle: $target"
Write-Output "Evidence: $fixture"
