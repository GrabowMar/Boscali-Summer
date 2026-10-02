using System.IO;
using BoscaliSummer.Modules.Support.Presentation.Window;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>Two cosmetic atlases and one physical surface. Art never describes observed intelligence.</summary>
    internal static class OpsArtwork
    {
        private static readonly Texture2D[] textures = new Texture2D[2];
        private static readonly Sprite[] sprites = new Sprite[8];
        private static readonly Material[] terrainMaterials = new Material[2];
        private static Shader terrainShader;
        private static bool shaderTried;
        private static Texture2D surface;
        private static bool surfaceTried;
        private static int users;
        public static void Acquire() => users++;
        public static void Release()
        {
            if (--users > 0) return;
            users = 0;
            for (int i = 0; i < sprites.Length; i++)
            { if (sprites[i] != null) Object.Destroy(sprites[i]); sprites[i] = null; }
            for (int i = 0; i < textures.Length; i++)
            { if (textures[i] != null) Object.Destroy(textures[i]); textures[i] = null; }
            for (int i = 0; i < terrainMaterials.Length; i++)
            { if (terrainMaterials[i] != null) Object.Destroy(terrainMaterials[i]); terrainMaterials[i] = null; }
            if (terrainShader != null) Object.Destroy(terrainShader);
            if (surface != null) Object.Destroy(surface);
            surface = null; surfaceTried = false;
            terrainShader = null; shaderTried = false;
        }

        public static Material TerrainMaterial(bool warm)
        {
            int index = warm ? 1 : 0;
            if (terrainMaterials[index] != null) return terrainMaterials[index];
            if (!shaderTried)
            {
                shaderTried = true;
                using (Stream stream = typeof(OpsArtwork).Assembly.GetManifestResourceStream("BoscaliSummer.Support.support-ui.bundle"))
                {
                    if (stream != null && stream.Length <= 1024 * 1024)
                        using (var bytes = new MemoryStream())
                        {
                            stream.CopyTo(bytes);
                            AssetBundle bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                            if (bundle != null)
                            {
                                terrainShader = bundle.LoadAsset<Shader>("Assets/ExpeditionTerrain.shader");
                                bundle.Unload(false);
                            }
                        }
                }
            }
            if (terrainShader == null || !terrainShader.isSupported) return null;
            var material = new Material(terrainShader) { name = warm ? "OPS field relief" : "OPS network relief" };
            material.SetColor("_Low", RoomPaint.Ink(warm ? "room-terrain-field-low" : "room-terrain-net-low", Color.gray));
            material.SetColor("_High", RoomPaint.Ink(warm ? "room-terrain-field-high" : "room-terrain-net-high", Color.white));
            material.SetColor("_Sea", RoomPaint.Ink("room-terrain-sea", Color.black));
            terrainMaterials[index] = material;
            return material;
        }

        public static Image Draw(RectTransform parent, Rect at, int index)
        {
            Image image = Chrome.Panel(parent, at, Color.white, Get(index));
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (image.sprite == null) image.enabled = false;
            return image;
        }

        public static RawImage DrawSurface(RectTransform parent, Rect at, float opacity)
        {
            if (at.width <= 0 || at.height <= 0) return null;
            if (surface == null && !surfaceTried)
            {
                surfaceTried = true;
                using (Stream stream = typeof(OpsArtwork).Assembly.GetManifestResourceStream("BoscaliSummer.Support.expedition-panel-surface-v1.png"))
                {
                    if (stream == null || stream.Length > 4 * 1024 * 1024) return null;
                    using (var bytes = new MemoryStream())
                    {
                        stream.CopyTo(bytes);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(bytes.ToArray(), true) || texture.width > 2048 || texture.height > 2048)
                        { Object.Destroy(texture); return null; }
                        texture.name = "OPS machined panel surface";
                        texture.wrapMode = TextureWrapMode.Repeat;
                        surface = texture;
                    }
                }
            }
            if (surface == null) return null;
            var image = Chrome.Graphic<RawImage>(parent, at, "MachinedPanelSurface");
            image.texture = surface;
            image.uvRect = new Rect(0, 0, at.width / 512f, at.height / 512f);
            image.color = Color.white.WithAlpha(Mathf.Clamp01(opacity));
            image.raycastTarget = false;
            return image;
        }

        public static void DrawPayload(RectTransform parent, Rect at)
        {
            Sprite sprite = Get(1);
            if (sprite == null) return;
            var go = new GameObject("PayloadEquipment", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            Chrome.Place(image.rectTransform, at);
            image.texture = sprite.texture;
            image.uvRect = new Rect(0.5f, 0.65f, 0.5f, 0.2f);
            image.raycastTarget = false;
        }

        private static Sprite Get(int index)
        {
            if (index < 0 || index >= sprites.Length) return null;
            if (sprites[index] != null) return sprites[index];
            int atlas = index / 4;
            if (textures[atlas] == null)
            {
                string file = atlas == 0 ? "expedition-hardware.png" : "expedition-crew.png";
                using (Stream stream = typeof(OpsArtwork).Assembly.GetManifestResourceStream("BoscaliSummer.Support." + file))
                {
                    if (stream == null || stream.Length > 12 * 1024 * 1024) return null;
                    using (var bytes = new MemoryStream())
                    {
                        stream.CopyTo(bytes);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(bytes.ToArray(), true) || texture.width > 4096 || texture.height > 4096)
                        { Object.Destroy(texture); return null; }
                        texture.name = file; texture.wrapMode = TextureWrapMode.Clamp;
                        textures[atlas] = texture;
                    }
                }
            }
            Texture2D source = textures[atlas];
            int tile = index % 4;
            sprites[index] = Sprite.Create(source,
                new Rect((tile % 2) * source.width / 2f, (tile < 2 ? 1 : 0) * source.height / 2f,
                    source.width / 2f, source.height / 2f), new Vector2(0.5f, 0.5f));
            sprites[index].name = "ExpeditionArt" + index;
            return sprites[index];
        }
    }
}
