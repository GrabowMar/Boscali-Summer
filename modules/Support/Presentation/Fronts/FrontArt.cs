using System.Collections.Generic;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The OPS window's painted art (Assets/Art, see its PROVENANCE.txt): the ORBIT and NETWORK column grounds, the SHADOW dossier sheet and recon prints,
    /// the three command emblems. Loaded once from the embedded PNGs; a missing or bad one is null and the caller keeps its drawn look.
    /// </summary>
    internal static class FrontArt
    {
        private const int MaxSide = 1024, MaxBytes = 2 << 20;
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            if (cache.TryGetValue(name, out Texture2D texture)) return texture;
            byte[] data = EmbeddedResources.ReadAll(typeof(FrontArt).Assembly, "BoscaliSummer.Support.Art." + name + ".png", MaxBytes);
            if (data == null || !PngSprites.TryLoad(data, MaxSide, "OpsArt." + name, out texture, out Sprite sprite)) texture = null;
            else texture.hideFlags = sprite.hideFlags = HideFlags.HideAndDontSave;
            return cache[name] = texture;
        }

        /// <summary>A raw image of <paramref name="name"/> on (x, y, w, h), cropped to the rect's aspect (never stretched); null when the art is missing.</summary>
        public static RawImage Fill(RectTransform parent, string name, float x, float y, float w, float h, Color tint)
        {
            Texture2D texture = Get(name);
            if (texture == null) return null;
            var go = new GameObject("Art " + name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var raw = go.AddComponent<RawImage>();
            raw.texture = texture;
            raw.color = tint;
            raw.raycastTarget = false;
            Place(raw, x, y, w, h);
            return raw;
        }

        /// <summary>Moves a <see cref="Fill"/> to a new rect and re-crops it, centred.</summary>
        public static void Place(RawImage raw, float x, float y, float w, float h)
        {
            AvLay.Place(raw.rectTransform, x, y, w, h);
            float art = (float)raw.texture.width / raw.texture.height, box = w / Mathf.Max(1f, h);
            raw.uvRect = box > art ? new Rect(0f, 0.5f - art / box * 0.5f, 1f, art / box) : new Rect(0.5f - box / art * 0.5f, 0f, box / art, 1f);
        }

        /// <summary>
        /// Lays a ground under a <see cref="FrontBox"/>'s body: straight above its frame (child 0), so every line, card and word of the box draws over it.
        /// It stops short of the bottom chamfer.
        /// </summary>
        public static void Ground(RectTransform box, string name, Color tint)
        {
            Vector2 size = box.rect.size;
            RawImage raw = Fill(box, name, 1f, FrontKit.HeaderH + 1f, size.x - 2f, size.y - FrontKit.HeaderH - 8f, tint);
            if (raw != null) raw.transform.SetSiblingIndex(1);
        }

        public static string Emblem(Front f) => f == Front.Space ? "ops_emblem_space" : f == Front.Cyber ? "ops_emblem_cyber" : "ops_emblem_sof";
    }
}
