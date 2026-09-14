#if UNITY_5_3_OR_NEWER
// Run with Run-ParachuteUnityCheck.ps1. Builds the production parachute mesh and
// renders offscreen views so the canopy/shroud geometry can be eyeballed.
using System;
using System.IO;
using BoscaliSummer.Garrisons;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public static class ParachuteUnityCheck
{
    private static string outputDir;
    private static int checks;

    public static void Run()
    {
        outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "preview"));
        Directory.CreateDirectory(outputDir);

        Mesh mesh = ParachuteMeshBuilder.Build();
        int canopyTris = mesh.GetTriangles(0).Length / 3;
        int shroudTris = mesh.GetTriangles(1).Length / 3;
        Bounds bounds = mesh.bounds;

        Debug.Log($"[ParachuteCheck] verts={mesh.vertexCount} subMeshes={mesh.subMeshCount} canopyTris={canopyTris} shroudTris={shroudTris} bounds={bounds}");

        Check(mesh.vertexCount > 0, "mesh has vertices");
        Check(mesh.subMeshCount == 2, "canopy and shroud submeshes exist");
        Check(canopyTris > 0 && shroudTris > 0, "both submeshes have triangles");
        Check(Mathf.Abs(bounds.size.y - (ParachuteMeshBuilder.RimHeight + ParachuteMeshBuilder.CanopyDepth)) < 0.2f, "rig height matches rim + canopy depth");
        Check(Mathf.Abs(bounds.max.x - ParachuteMeshBuilder.RimRadius) < 0.2f, "canopy half-span matches rim radius");

        foreach (Vector3 normal in mesh.normals)
            CheckVectors(normal);

        Render("parachute-side", new Vector3(11f, 5.5f, 0.4f), new Vector3(0f, 5.5f, 0f), 45f);
        Render("parachute-three-quarter", new Vector3(12f, 8f, 12f), new Vector3(0f, 5.5f, 0f), 45f);
        Render("parachute-below", new Vector3(0.2f, 1.2f, 12f), new Vector3(0f, 6f, 0f), 50f);

        Debug.Log($"[ParachuteCheck] {checks} assertions passed. Previews: {outputDir}");
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            Debug.LogError("[ParachuteCheck] FAIL: " + message);
#if UNITY_EDITOR
            EditorApplication.Exit(1);
#endif
        }
    }

    private static void CheckVectors(Vector3 normal)
    {
        if (float.IsNaN(normal.x) || float.IsNaN(normal.y) || float.IsNaN(normal.z) ||
            float.IsInfinity(normal.x) || float.IsInfinity(normal.y) || float.IsInfinity(normal.z))
        {
            Check(false, "mesh normals are finite");
        }
    }

    private static void Render(string name, Vector3 cameraPosition, Vector3 lookAt, float fieldOfView)
    {
        var root = new GameObject("ParachutePreview");
        GameObject visual = new GameObject("Parachute");
        visual.transform.SetParent(root.transform, false);
        MeshFilter filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = ParachuteMeshBuilder.Build();

        Material fabric = new Material(Shader.Find("Standard"));
        fabric.color = new Color(0.32f, 0.4f, 0.24f);
        Material shroud = new Material(Shader.Find("Standard"));
        shroud.color = new Color(0.78f, 0.78f, 0.72f);

        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { fabric, shroud };

        var lightGo = new GameObject("PreviewLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;

        var cameraGo = new GameObject("PreviewCamera");
        cameraGo.transform.SetParent(root.transform, false);
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.45f, 0.62f, 0.8f);
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;
        cameraGo.transform.position = cameraPosition;
        cameraGo.transform.LookAt(lookAt);

        var renderTexture = new RenderTexture(900, 700, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = renderTexture;
        camera.Render();

        RenderTexture.active = renderTexture;
        var texture = new Texture2D(900, 700, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 900, 700), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(outputDir, name + ".png"), texture.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = null;
        renderTexture.Release();
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(renderTexture);
        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Object.DestroyImmediate(fabric);
        UnityEngine.Object.DestroyImmediate(shroud);
    }
}
#endif
