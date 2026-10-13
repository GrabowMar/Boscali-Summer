#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline miniature sheet: writes every <see cref="MapGlyph"/> shape through the real
/// <see cref="MapMinis"/> batch (both sides, grounded and airborne, several headings) and renders it
/// from the relief's kind of tilted orthographic view. Checks that every shape batches, stays inside
/// its vertex budget and lands in one mesh. The bare check project has no URP, so the sheet swaps in
/// the built-in vertex-colour particle shader; in-game shading still needs a look in game.
/// </summary>
public static class MapMinisUnityCheck
{
    private const int Width = 1600, Height = 760;
    private const int ShapeBudget = 200;

    public static void Run()
    {
        try
        {
            var stand = new Material(Shader.Find("Particles/Standard Unlit")) { mainTexture = Texture2D.whiteTexture };
            stand.SetFloat("_Cull", 0f);
            typeof(MapMinis).GetField("material", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, stand);

            var scene = new GameObject("Scene");
            Color32[] tints = { new Color32(51, 153, 255, 255), new Color32(255, 71, 61, 255) };
            Array glyphs = Enum.GetValues(typeof(MapGlyph));
            MapMinis.Begin(scene.transform, 0);
            for (int row = 0; row < 4; row++)
            {
                int column = 0;
                foreach (MapGlyph glyph in glyphs)
                {
                    float heading = (row * 40f + column * 25f) * Mathf.Deg2Rad;
                    var forward = row == 0 ? Vector2.up : new Vector2(-Mathf.Sin(heading), Mathf.Cos(heading));
                    var at = new Vector3((column - (glyphs.Length - 1) * .5f) * 1.7f, row == 3 ? .8f : 0f, (1.5f - row) * 1.9f);
                    if (!MapMinis.Add(glyph, at, forward, 1f, tints[row % 2], row != 3))
                        throw new Exception("Shape " + glyph + " did not batch.");
                    column++;
                }
            }
            MapMinis.End();
            // The cost contract: the same contacts again (they moved, or the zoom changed) must resend vertices only,
            // and a full map's worth of rewrites must stay far below a frame.
            int fullBefore = MapMinis.FullUploads;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const int Rounds = 50, Contacts = 600;
            for (int round = 0; round < Rounds; round++)
            {
                MapMinis.Begin(scene.transform, 0);
                for (int i = 0; i < Contacts; i++)
                    MapMinis.Add((MapGlyph)(i % glyphs.Length), new Vector3(i % 30 * 3f + round * .01f, 0f, i / 30 * 3f + 40f),
                        Vector2.up, 1f + round * .001f, tints[i % 2], true);
                MapMinis.End();
            }
            double perRewrite = clock.Elapsed.TotalMilliseconds / Rounds;
            if (MapMinis.FullUploads - fullBefore != 1)
                throw new Exception("A rewrite of the same contacts resent the index buffer: " + (MapMinis.FullUploads - fullBefore) + " full uploads in " + Rounds + " rewrites, wanted 1.");
            if (perRewrite > 2.0) throw new Exception("Rewriting " + Contacts + " miniatures took " + perRewrite.ToString("0.000") + " ms; the budget is 2 ms.");
            string cost = Contacts + " miniatures rewrite in " + perRewrite.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms, index buffer sent once in " + Rounds + " rewrites";
            // Back to the sheet for the picture.
            MapMinis.Begin(scene.transform, 0);
            for (int row = 0; row < 4; row++)
            {
                int column = 0;
                foreach (MapGlyph glyph in glyphs)
                {
                    float heading = (row * 40f + column * 25f) * Mathf.Deg2Rad;
                    var forward = row == 0 ? Vector2.up : new Vector2(-Mathf.Sin(heading), Mathf.Cos(heading));
                    var at = new Vector3((column - (glyphs.Length - 1) * .5f) * 1.7f, row == 3 ? .8f : 0f, (1.5f - row) * 1.9f);
                    MapMinis.Add(glyph, at, forward, 1f, tints[row % 2], row != 3);
                    column++;
                }
            }
            MapMinis.End();
            MeshFilter[] filters = scene.GetComponentsInChildren<MeshFilter>();
            if (filters.Length != 1) throw new Exception("Miniatures must share one renderer, found " + filters.Length);
            int written = filters[0].sharedMesh.vertexCount;
            if (written == 0 || written > glyphs.Length * 4 * ShapeBudget)
                throw new Exception("Unexpected miniature vertex count: " + written);

            var cam = new GameObject("Cam", typeof(Camera)).GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4.6f;
            cam.transform.position = Quaternion.Euler(52f, 20f, 0f) * new Vector3(0f, 0f, -40f);
            cam.transform.LookAt(Vector3.zero);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.19f, .27f, .31f);
            var target = new RenderTexture(Width, Height, 24);
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            var output = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            output.Apply();
            File.WriteAllBytes("minis-sheet.png", output.EncodeToPNG());
            File.WriteAllText("result.txt", "PASS: " + glyphs.Length + " shapes x 4 rows batched into one mesh of " +
                written + " vertices (" + written / (glyphs.Length * 4) + " per miniature); " + cost + ".");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }
}
#endif
