#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NOAvionics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// The tail-fin insignia bake against synthetic fins, through the production <c>TailPaint.Bake</c> and the
/// shipped shader bundle. A four-colour marker stands in for the insignia so orientation, the mirrored
/// inboard face, shared UV islands and UVs past the 0..1 tile are each provable from read-back texels.
/// Offline fixture only: no aircraft, no livery, no game session.
/// </summary>
public static class TailPaintUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Atlas = 256;
    private static readonly Assembly Mod = typeof(AvConsole).Assembly;
    private static int assertions;
    private static Texture2D marker;

    public static void Run()
    {
        try
        {
            Type paintType = Mod.GetType("BoscaliSummer.Modules.Progression.Visuals.TailPaint", true);
            var host = new GameObject("TailPaint");
            var paint = (Behaviour)host.AddComponent(paintType);
            paint.enabled = false;
            That((bool)Call(paint, "BakeReady"), "the embedded emblem.bundle must load and its shader be supported");
            marker = Marker();

            // Separate islands: the outboard face owns the left half of the atlas, the inboard face the right half.
            Texture2D separate = Bake(paint, paintType, Fin(shared: false, uShift: 0f), "separate");
            Expect(separate, 0.20f, 0.30f, Color.red, "outboard face, viewer's top-left");
            Expect(separate, 0.30f, 0.30f, Color.green, "outboard face, viewer's top-right");
            Expect(separate, 0.20f, 0.20f, Color.blue, "outboard face, viewer's bottom-left");
            Expect(separate, 0.30f, 0.20f, Color.yellow, "outboard face, viewer's bottom-right");
            Expect(separate, 0.70f, 0.30f, Color.red, "inboard face reads un-mirrored from its own side");
            Expect(separate, 0.80f, 0.30f, Color.green, "inboard face, viewer's top-right");
            Expect(separate, 0.70f, 0.20f, Color.blue, "inboard face, viewer's bottom-left");
            Expect(separate, 0.05f, 0.05f, Color.gray, "livery outside the marking is untouched");
            Expect(separate, 0.25f, 0.75f, Color.gray, "texels no fin maps to are untouched");

            // One mirrored island for both faces: painted once (outboard), never doubled by the inboard pass.
            Texture2D shared = Bake(paint, paintType, Fin(shared: true, uShift: 0f), "shared");
            Expect(shared, 0.20f, 0.30f, Color.red, "shared island keeps the outboard painting (top-left)");
            Expect(shared, 0.30f, 0.30f, Color.green, "shared island keeps the outboard painting (top-right)");
            Expect(shared, 0.30f, 0.20f, Color.yellow, "shared island keeps the outboard painting (bottom-right)");

            // UVs one tile over (the atlas repeats) land on the same texels.
            Texture2D wrapped = Bake(paint, paintType, Fin(shared: false, uShift: 1f), "wrapped");
            Expect(wrapped, 0.20f, 0.30f, Color.red, "a fin unwrapped past u = 1 is painted on the wrapped texels");
            Expect(wrapped, 0.80f, 0.20f, Color.yellow, "a wrapped inboard face is painted too");

            File.WriteAllText("result.txt", "PASS: " + assertions + " assertions over 3 synthetic fins through the production bake and pack (DXT5 and DXT1). " +
                "Offline fixture only; no live game, aircraft or livery claim.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            File.WriteAllText("result.txt", "FAIL: " + inner);
            Debug.LogException(inner);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Top-left red, top-right green, bottom-left blue, bottom-right yellow; opaque.</summary>
    private static Texture2D Marker()
    {
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                texture.SetPixel(x, y, y >= 32 ? (x < 32 ? Color.red : Color.green) : (x < 32 ? Color.blue : Color.yellow));
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// A 2 m x 2 m plate, 0.1 m thick, in the YZ plane: outboard face at x = +0.05 (normal +X), inboard at
    /// x = -0.05. Seen from outboard, right is +Z. Each face's UVs are laid out as its own viewer sees it,
    /// unless <paramref name="shared"/>, where the inboard face reuses the outboard UVs (a mirrored island).
    /// </summary>
    private static Mesh Fin(bool shared, float uShift)
    {
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        for (int face = 0; face < 2; face++)
        {
            float x = face == 0 ? 0.05f : -0.05f;
            Vector3 normal = face == 0 ? Vector3.right : Vector3.left;
            int first = vertices.Count;
            foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
            {
                float z = corner.x, y = corner.y;
                vertices.Add(new Vector3(x, y, z)); normals.Add(normal);
                float across = face == 0 || shared ? z : -z;           // viewer's right: +Z from outboard, -Z from inboard
                float u = (across + 1f) * 0.25f + (face == 1 && !shared ? 0.5f : 0f);
                uv.Add(new Vector2(u + uShift, (y + 1f) * 0.25f));
            }
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        var mesh = new Mesh { name = "fixture_fin" };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
        return mesh;
    }

    private static Texture2D Bake(Behaviour paint, Type paintType, Mesh fin, string name)
    {
        Type spotType = paintType.GetNestedType("Spot", All);
        // Centre on the outboard face, normal +X, up +Y, half a metre: with the 0.9 margin the marking spans 0.9 m of a 2 m face.
        object spot = Activator.CreateInstance(spotType, All, null, new object[] { 0.05f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0.5f, false }, null);
        var fins = (IList)Get(paint, "fins");
        fins.Clear();
        fins.Add(Activator.CreateInstance(typeof(KeyValuePair<,>).MakeGenericType(typeof(Mesh), spotType), fin, spot));

        // The stock atlases come both ways: opaque DXT1 and alpha-carrying DXT5. The copy must follow its source.
        bool opaque = name == "shared";
        var livery = new Texture2D(Atlas, Atlas, opaque ? TextureFormat.RGB24 : TextureFormat.RGBA32, false) { name = "fixture_livery_" + name, wrapMode = TextureWrapMode.Repeat };
        var grey = new Color32[Atlas * Atlas];
        for (int i = 0; i < grey.Length; i++) grey[i] = new Color32(128, 128, 128, 255);
        livery.SetPixels32(grey); livery.Apply();
        if (opaque) livery.Compress(false);

        object marking = Activator.CreateInstance(paintType.GetNestedType("Marking", All));
        object coat = Call(paint, "Bake", livery, marker, marking);
        That(coat != null, name + ": the bake returned no coat");
        var painted = (RenderTexture)Field(coat, "Painted");
        That(painted != null && painted.width == Atlas && painted.height == Atlas && painted.useMipMap && painted.depth == 0,
            name + ": the working copy must match the livery, carry mips and keep no depth buffer");
        That(painted.wrapMode == TextureWrapMode.Repeat, name + ": the copy must wrap like the livery it replaces");
        Texture2D unpacked = Read(painted);

        // Pack it the way the game session does: read back, encode off-thread, swap, free the working copy.
        var coats = (IList)Get(paint, "coats");
        coats.Clear();
        coats.Add(coat);
        Call(paint, "StartReadback", coat);
        UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
        var packing = (System.Threading.Tasks.Task<byte[]>)Field(coat, "Packing");
        That(packing != null, name + ": the read-back did not start an encode");
        packing.Wait();
        Call(paint, "AdvancePacking");
        var packed = (Texture2D)Field(coat, "Packed");
        That(packed != null && Field(coat, "Painted") == null, name + ": the packed copy must take over and the working copy go");
        That(packed.format == (opaque ? TextureFormat.DXT1 : TextureFormat.DXT5) && packed.mipmapCount == 9 &&
            packed.width == Atlas && packed.wrapMode == TextureWrapMode.Repeat, name + ": the packed copy is the wrong format: " + packed.format);
        Texture2D read = Read(packed);
        // Flat paints survive packing to within 5:6:5 rounding, and rows keep their order.
        foreach (Vector2 uv in new[] { new Vector2(0.20f, 0.30f), new Vector2(0.30f, 0.20f), new Vector2(0.05f, 0.05f), new Vector2(0.25f, 0.75f) })
        {
            Color before = unpacked.GetPixel(Mathf.RoundToInt(uv.x * Atlas), Mathf.RoundToInt(uv.y * Atlas));
            Color after = read.GetPixel(Mathf.RoundToInt(uv.x * Atlas), Mathf.RoundToInt(uv.y * Atlas));
            That(Mathf.Abs(before.r - after.r) < 0.03f && Mathf.Abs(before.g - after.g) < 0.03f && Mathf.Abs(before.b - after.b) < 0.03f,
                name + ": packing changed the texel at " + uv + " from " + before + " to " + after);
        }
        File.WriteAllBytes("tailpaint-" + name + ".png", read.EncodeToPNG());
        coats.Clear();
        Object.DestroyImmediate(packed);
        Object.DestroyImmediate(livery);
        return read;
    }

    /// <summary>Any texture as readable pixels, through a blit, so a packed (unreadable) copy can be asserted on too.</summary>
    private static Texture2D Read(Texture texture)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(Atlas, Atlas, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(texture, target);
        RenderTexture.active = target;
        var read = new Texture2D(Atlas, Atlas, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, Atlas, Atlas), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        return read;
    }

    private static void Expect(Texture2D atlas, float u, float v, Color want, string what)
    {
        Color got = atlas.GetPixel(Mathf.RoundToInt(u * Atlas), Mathf.RoundToInt(v * Atlas));
        // The marking goes on at 94 % over mid grey, so a painted texel is within a few percent of the pure colour.
        bool match = Mathf.Abs(got.r - want.r) < 0.12f && Mathf.Abs(got.g - want.g) < 0.12f && Mathf.Abs(got.b - want.b) < 0.12f;
        That(match, what + ": expected " + want + " at uv (" + u + ", " + v + "), read " + got);
    }

    private static void That(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
}
#endif
