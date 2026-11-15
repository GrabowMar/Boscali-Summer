using System;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Immersion.Configuration;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    internal static class PilotShaderBundle
    {
        private static AssetBundle bundle, models;
        private static Shader body, reflection, visor, ink;
        private static readonly Mesh[] firstPerson = new Mesh[4];
        private static readonly Texture2D[] bodyAlbedo = new Texture2D[4];
        private static readonly string[] suffixes = { "", "-recon", "-rescue", "-nightops" };
        private static Texture2D bodyNormal, bodyMask;
        private static Texture2D boardAlbedo, boardNormal, boardMask;
        private static bool attempted;
        internal static Shader GetBody() { Load(); return body; }
        internal static Shader GetReflection() { Load(); return reflection; }
        internal static Shader GetVisor() { Load(); return visor; }
        internal static Shader GetKneeboardInk() { Load(); return ink; }
        internal static Mesh GetFirstPersonMesh(PilotModelStyle style = PilotModelStyle.Standard)
        { Load(); return firstPerson[Index(style)] ?? firstPerson[0]; }
        private static int Index(PilotModelStyle style) => (uint)style < 4 ? (int)style : 0;

        /// <summary>The authored body's own textures; they ship with (and only match) the authored first-person mesh.</summary>
        internal static bool GetBodyTextures(out Texture2D albedo, out Texture2D normal, out Texture2D mask)
            => GetBodyTextures(PilotModelStyle.Standard, out albedo, out normal, out mask);

        internal static bool GetBodyTextures(PilotModelStyle style, out Texture2D albedo, out Texture2D normal, out Texture2D mask)
        {
            Load();
            albedo = bodyAlbedo[Index(style)] ?? bodyAlbedo[0]; normal = bodyNormal; mask = bodyMask;
            return albedo != null && normal != null && mask != null;
        }
        internal static bool GetKneeboardTextures(out Texture2D albedo, out Texture2D normal, out Texture2D mask)
        {
            Load(); albedo = boardAlbedo; normal = boardNormal; mask = boardMask;
            return albedo != null && normal != null && mask != null;
        }
        internal static void Release()
        {
            if (bundle != null) bundle.Unload(true);
            if (models != null) models.Unload(true);
            bundle = models = null; body = reflection = visor = null; attempted = false;
            ClearArt();
        }
        private static void ClearArt()
        {
            Array.Clear(firstPerson, 0, firstPerson.Length); Array.Clear(bodyAlbedo, 0, bodyAlbedo.Length);
            bodyNormal = bodyMask = null;
            boardAlbedo = boardNormal = boardMask = null;
            ink = null;
        }
        private static void Load()
        {
            if (attempted) return;
            attempted = true;
            try
            {
                byte[] data = EmbeddedResources.ReadAll(
                    typeof(PilotShaderBundle).Assembly, "BoscaliSummer.Immersion.pilot.bundle", 4 * 1024 * 1024);
                if (data == null) return;
                bundle = AssetBundle.LoadFromMemory(data);
                if (bundle == null) return;
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                for (int i = 0; i < shaders.Length; i++)
                {
                    Shader shader = shaders[i];
                    if (!shader.isSupported) continue;
                    if (shader.name == "Boscali/PilotBody") body = shader;
                    if (shader.name == "Boscali/PilotCanopyReflection") reflection = shader;
                    if (shader.name == "Boscali/VisorOverlay") visor = shader;
                }
                // Optional model art must never take working pilot/visor shaders down with it.
                try
                {
                    byte[] modelData = EmbeddedResources.ReadAll(typeof(PilotShaderBundle).Assembly,
                        "BoscaliSummer.Immersion.pilot-models.bundle", 24 * 1024 * 1024);
                    if (modelData != null) models = AssetBundle.LoadFromMemory(modelData);
                    if (models != null) LoadArt(models);
                }
                catch (Exception)
                {
                    ClearArt();
                    if (models != null) models.Unload(true);
                    models = null;
                }
                if (models == null)
                {
                    try { LoadArt(bundle); }
                    catch (Exception) { ClearArt(); }
                }
            }
            catch (Exception) { Release(); attempted = true; }
        }
        private static void LoadArt(AssetBundle art)
        {
            for (int i = 0; i < firstPerson.Length; i++)
            {
                Mesh mesh = art.LoadAsset<Mesh>("assets/pilotshader/pilot-first-person" + suffixes[i] + ".asset");
                if (mesh != null && mesh.vertexCount <= 50000 && mesh.subMeshCount == 1) firstPerson[i] = mesh;
                string textureSuffix = suffixes[i].Replace('-', '_');
                bodyAlbedo[i] = art.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_albedo" + textureSuffix + ".png");
            }
            bodyNormal = art.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_normal.png");
            bodyMask = art.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_mask.png");
            boardAlbedo = art.LoadAsset<Texture2D>("assets/pilotshader/body/kneeboard_albedo.png");
            boardNormal = art.LoadAsset<Texture2D>("assets/pilotshader/body/kneeboard_normal.png");
            boardMask = art.LoadAsset<Texture2D>("assets/pilotshader/body/kneeboard_mask.png");
            Shader cardInk = art.LoadAsset<Shader>("assets/pilotshader/kneeboardink.shader");
            if (cardInk != null && cardInk.isSupported) ink = cardInk;
        }
    }
}
