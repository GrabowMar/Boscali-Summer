using System;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    internal static class PilotShaderBundle
    {
        private static AssetBundle bundle;
        private static Shader body, reflection;
        private static Mesh firstPerson;
        private static bool attempted;
        internal static Shader GetBody() { Load(); return body; }
        internal static Shader GetReflection() { Load(); return reflection; }
        internal static Mesh GetFirstPersonMesh() { Load(); return firstPerson; }
        internal static void Release()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null; body = reflection = null; firstPerson = null; attempted = false;
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
                }
                firstPerson = bundle.LoadAsset<Mesh>("assets/pilotshader/pilot-first-person.asset");
                if (firstPerson != null && (firstPerson.vertexCount > 16384 || firstPerson.subMeshCount != 1)) firstPerson = null;
            }
            catch (Exception) { body = reflection = null; firstPerson = null; }
        }
    }
}
