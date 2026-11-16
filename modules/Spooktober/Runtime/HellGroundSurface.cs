using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Shares the existing charcoal-and-pale-ash wasteland appearance, with its original world-metre UV scale.</summary>
    internal static class HellGroundSurface
    {
        private static Material shared, infection;
        internal static string SourceName { get; private set; } = "";

        internal static Material Material()
        {
            if (shared != null) return shared;
            Shader shader = SpookAssets.AshSurface;
            if (shader == null) return null;
            shared = new Material(shader) { name = "Hell Sooted Wasteland" };
            SourceName = shader.name;
            return shared;
        }

        /// <summary>Transparent territory overlay. UV2 carries arrival seconds and coverage; a property block sets each field's clock.</summary>
        internal static Material InfectionMaterial()
        {
            if (infection != null) return infection;
            Shader shader = SpookAssets.HellInfection;
            if (shader == null) return null;
            infection = new Material(shader) { name = "Hell Branching Infection" };
            return infection;
        }

        /// <summary>The reused ash shader expects global metres, preserving its original grain scale through origin shifts.</summary>
        internal static Vector2 Uv(GlobalPosition at) => new Vector2(at.x, at.z);

        /// <summary>After every ground consumer is gone; the shader remains owned by SpookAssets.</summary>
        internal static void Release()
        {
            if (shared != null) UnityEngine.Object.Destroy(shared);
            if (infection != null) UnityEngine.Object.Destroy(infection);
            shared = infection = null;
            SourceName = "";
        }
    }
}
