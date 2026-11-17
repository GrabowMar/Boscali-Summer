#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>One explicit sandbox inventory; never replaces or modifies game assets.</summary>
    public static class AssetBindingProbe
    {
        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return AutomationArgs.Failed("requires a nomodkit scenario");
            var textures = new List<object>();
            foreach (Texture texture in Resources.FindObjectsOfTypeAll<Texture>())
                if (texture != null && texture.name.IndexOf("crinkle", StringComparison.OrdinalIgnoreCase) >= 0 && textures.Count < 32)
                    textures.Add(new Dictionary<string, object> { ["name"] = texture.name, ["type"] = texture.GetType().Name,
                        ["width"] = texture.width, ["height"] = texture.height });
            var materials = new List<object>();
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material == null || material.shader == null || materials.Count >= 64 ||
                    material.shader.name.IndexOf("ShipTilingShader", StringComparison.Ordinal) < 0) continue;
                bool property = material.HasProperty("_Normal_Crinkle");
                Texture texture = property ? material.GetTexture("_Normal_Crinkle") : null;
                materials.Add(new Dictionary<string, object> { ["name"] = material.name, ["shader"] = material.shader.name,
                    ["hasCrinkleProperty"] = property, ["crinkleTexture"] = texture != null ? texture.name : "",
                    ["textureType"] = texture != null ? texture.GetType().Name : "" });
            }
            var after = new List<string>();
            foreach (Texture texture in Resources.FindObjectsOfTypeAll<Texture>())
                if (texture != null && texture.name.IndexOf("crinkle", StringComparison.OrdinalIgnoreCase) >= 0 && after.Count < 32)
                    after.Add(texture.name + " | " + texture.GetType().Name);
            return new Dictionary<string, object> { ["ok"] = true, ["crinkleTextures"] = textures,
                ["shipMaterials"] = materials, ["crinkleAfterMaterialRead"] = after };
        }
    }
}
#endif
