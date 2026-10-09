using System;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    internal static class PilotShaderBundle
    {
        private static AssetBundle bundle;
        private static Shader body, reflection, visor;
        private static Mesh firstPerson;
        private static Texture2D bodyAlbedo, bodyNormal, bodyMask;
        private static bool attempted;
        internal static Shader GetBody() { Load(); return body; }
        internal static Shader GetReflection() { Load(); return reflection; }
        internal static Shader GetVisor() { Load(); return visor; }
        internal static Mesh GetFirstPersonMesh() { Load(); return firstPerson; }

        /// <summary>The authored body's own textures; they ship with (and only match) the authored first-person mesh.</summary>
        internal static bool GetBodyTextures(out Texture2D albedo, out Texture2D normal, out Texture2D mask)
        {
            Load();
            albedo = bodyAlbedo; normal = bodyNormal; mask = bodyMask;
            return albedo != null && normal != null && mask != null;
        }
        internal static void Release()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null; body = reflection = visor = null; firstPerson = null; attempted = false;
            bodyAlbedo = bodyNormal = bodyMask = null;
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
                firstPerson = bundle.LoadAsset<Mesh>("assets/pilotshader/pilot-first-person.asset");
                if (firstPerson != null && (firstPerson.vertexCount > 16384 || firstPerson.subMeshCount != 1)) firstPerson = null;
                bodyAlbedo = bundle.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_albedo.png");
                bodyNormal = bundle.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_normal.png");
                bodyMask = bundle.LoadAsset<Texture2D>("assets/pilotshader/body/pilot_mask.png");
            }
            catch (Exception) { body = reflection = visor = null; firstPerson = null; bodyAlbedo = bodyNormal = bodyMask = null; }
        }
    }
}
