using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The terrain ghost in the cockpit view. The cockpit interior is drawn by a second, overlay camera over the main one
    /// (near 0.01 m, far 5 m, culling mask the "Cockpit" and "CockpitAndExternal" layers), so its opaque parts cover anything the
    /// main camera drew, ghost included. The ghost is therefore drawn on that camera, squeezed along the sight lines of the main
    /// camera into its 5 m of depth: a vertex at view-space (x, y, z) of the main camera becomes (k*r*x, k*r*y, k*z), which the
    /// cockpit camera projects to the very same screen point (r is the ratio of the two lens tangents, 1 when the fields of view
    /// match, as the game keeps them). <see cref="Scales"/> gives k*r and k; the caller builds
    /// <c>inverse(V_cockpit) x Scale(k*r, k*r, k) x V_main x M</c>. No Unity types, so it is unit-tested.
    /// </summary>
    internal static class GhostCockpitMath
    {
        /// <summary>The game's "Cockpit" layer: preferred for the ghost when the cockpit camera sees it.</summary>
        internal const int PreferredLayer = 3;
        /// <summary>About the farthest drawn vertex from the viewer: the grid corner's reach plus the height difference, in metres.</summary>
        internal const float FarthestMetres = 15000f;
        /// <summary>Metres from the cockpit camera where the farthest vertex lands.</summary>
        internal const float TargetDepth = 4f;
        /// <summary>Never past this share of the cockpit camera's far plane, whatever it is.</summary>
        internal const float FarShare = .8f;

        /// <summary>The layer to draw on for a camera's culling mask: <see cref="PreferredLayer"/> if seen, else the lowest layer seen, else -1 (the camera sees nothing).</summary>
        internal static int Layer(int cullingMask)
        {
            if ((cullingMask & (1 << PreferredLayer)) != 0) return PreferredLayer;
            for (int layer = 0; layer < 32; layer++)
                if ((cullingMask & (1 << layer)) != 0) return layer;
            return -1;
        }

        /// <summary>The view-space scale for a squeeze into the cockpit camera: <paramref name="lateral"/> (x and y) is k*r, <paramref name="depth"/> (z) is k.</summary>
        internal static void Scales(float cockpitFov, float mainFov, float cockpitFar, out float lateral, out float depth)
        {
            float target = cockpitFar > 0f ? Math.Min(TargetDepth, cockpitFar * FarShare) : TargetDepth;
            depth = target / FarthestMetres;
            lateral = depth * LensRatio(cockpitFov, mainFov);
        }

        /// <summary>tan(cockpit fov / 2) over tan(main fov / 2), both vertical fields of view in degrees; 1 for a field of view that is not a usable lens.</summary>
        internal static float LensRatio(float cockpitFov, float mainFov)
        {
            if (!Usable(cockpitFov) || !Usable(mainFov)) return 1f;
            return MathF.Tan(cockpitFov * .5f * (MathF.PI / 180f)) / MathF.Tan(mainFov * .5f * (MathF.PI / 180f));
        }

        private static bool Usable(float fov) => fov > 1f && fov < 170f;
    }
}
