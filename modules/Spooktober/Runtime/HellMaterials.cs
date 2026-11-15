using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Three shared draws per alien hull/growth field: obsidian, exposed ribs and its molten interior.</summary>
    internal static class HellMaterials
    {
        private static Material[] shared;
        internal static Material[] Shared => shared ?? (shared = Create());

        private static Material[] Create()
        {
            Shader shader = SpookAssets.HellMatter ?? SpookAssets.Hellfire;
            if (shader == null) return new Material[3];
            var result = new Material[3];
            for (int i = 0; i < result.Length; i++)
            {
                var material = new Material(shader) { name = "Hell " + (i == 0 ? "Obsidian" : i == 1 ? "Exposed Ribs" : "Furnace") };
                material.SetColor("_Char", i == 0 ? new Color(.035f, .038f, .045f) : new Color(.095f, .063f, .052f));
                material.SetFloat("_Layer", i);
                material.SetFloat("_Intensity", i == 2 ? 2.1f : .18f);
                material.SetFloat("_NoiseScale", .65f);
                material.SetFloat("_FarDistance", 1000000f);
                material.SetFloat("_CrackWidth", .035f);
                result[i] = material;
            }
            return result;
        }

        internal static void Release()
        {
            if (shared != null) foreach (Material material in shared) if (material != null) Object.Destroy(material);
            shared = null;
        }
    }
}
