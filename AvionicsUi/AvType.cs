using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Kit v2 typography. Faces come from avionics-ui.bundle; when it is absent every face falls
    /// back to ONE vanilla font resolved deterministically from the game's MFD label (never "whatever
    /// the last panel built"), so a v2 console is always a single typeface.
    /// </summary>
    public static class AvType
    {
        private static readonly Dictionary<AvFace, TMP_FontAsset> Faces = new Dictionary<AvFace, TMP_FontAsset>(5);
        private static TMP_FontAsset vanilla;
        private static bool fallbacksLinked;

        public static TMP_FontAsset VanillaFallback
        {
            get
            {
                if (vanilla != null) return vanilla;
                MFDScreen screen = Object.FindObjectOfType<MFDScreen>();
                if (screen != null && screen.label != null) vanilla = screen.label.font;
                if (vanilla == null) vanilla = TMP_Settings.defaultFontAsset;
                return vanilla;
            }
        }

        public static TMP_FontAsset Face(AvFace face)
        {
            if (Faces.TryGetValue(face, out TMP_FontAsset f) && f != null) return f;
            f = AvBundle.Font(AvTypeScale.AssetName(face));
            if (f != null) { Faces[face] = f; LinkFallbacks(); return f; }
            return VanillaFallback;
        }

        public static void Apply(TMP_Text text, AvTextRole role)
        {
            if (text == null) return;
            AvTypeSpec spec = AvTypeScale.Of(role);
            TMP_FontAsset font = Face(spec.Face);
            if (font != null && text.font != font) text.font = font;
            text.fontSize = spec.Size;
            text.characterSpacing = spec.Tracking;
            text.fontStyle = spec.Upper ? FontStyles.UpperCase : FontStyles.Normal;
            text.enableAutoSizing = false;
            text.raycastTarget = false;
        }

        public static void ResetScene() { vanilla = null; }

        // Barlow lacks a few symbols (▲ ✕ →); JetBrains Mono has them; the vanilla face backs both.
        private static void LinkFallbacks()
        {
            if (fallbacksLinked) return;
            TMP_FontAsset cond = AvBundle.Font(AvTypeScale.AssetName(AvFace.Cond));
            TMP_FontAsset strong = AvBundle.Font(AvTypeScale.AssetName(AvFace.CondStrong));
            TMP_FontAsset mono = AvBundle.Font(AvTypeScale.AssetName(AvFace.Mono));
            if (cond == null || mono == null) return;
            fallbacksLinked = true;
            foreach (TMP_FontAsset f in new[] { cond, strong })
            {
                if (f == null) continue;
                if (f.fallbackFontAssetTable == null) f.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!f.fallbackFontAssetTable.Contains(mono)) f.fallbackFontAssetTable.Add(mono);
            }
            TMP_FontAsset v = VanillaFallback;
            if (v != null)
            {
                if (mono.fallbackFontAssetTable == null) mono.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!mono.fallbackFontAssetTable.Contains(v)) mono.fallbackFontAssetTable.Add(v);
            }
        }
    }
}
