using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>How visible each block of the board and the left column is this frame, 0..1: the visor's power-on brings them up one after another and its reactions (G dim, a glitch's flicker, an EMP's stutter) scale all of them by <see cref="All"/>.</summary>
    internal struct BoardLook
    {
        /// <summary>The board's blocks in reading order: the stores header (stores, power, expendables), the lamps and systems strip and notice line, the scope page, the wing page.</summary>
        public float Stores, Shell, Page, Wing;
        /// <summary>The left column's: the event log, the cues and route, the corner furniture (and the COMMS block, which follows the last).</summary>
        public float Log, Cue, Comms;
        /// <summary>The reactive visor's brightness over everything.</summary>
        public float All;

        public static BoardLook Rest => new BoardLook { Stores = 1f, Shell = 1f, Page = 1f, Wing = 1f, Log = 1f, Cue = 1f, Comms = 1f, All = 1f };
    }

    // The visor's power-on and reactions (VisorBootHud, VisorFx) bring the board and the left column up block by block and dim, flicker and shift them: only the mod's own groups are touched, by alpha (the game's HUD is never dimmed, moved or switched off).
    internal sealed partial class StatusPanel
    {
        private CanvasGroup routeGroup, cornerGroup, storesGroup, shellGroup, scopeGroup, statusGroup;
        private bool bootDimmed;

        /// <summary>A group the board owns that never takes input and combines with the one above it.</summary>
        private static CanvasGroup Quiet(GameObject go)
        {
            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            return group;
        }

        /// <summary>
        /// The blocks' visibility, every frame while anything is not at rest and once with <see cref="BoardLook.Rest"/> when it is: the board's root (all of it), then each block (the stores header, the lamps and systems strip, the scope page and the
        /// STATUS strip, the wing page's slots), and the left column's groups (the event log, the cue stack and route slot, the corner furniture). Every write compares first, so a steady frame does nothing here.
        /// </summary>
        public void ApplyLook(in BoardLook look)
        {
            Set(rootGroup, look.All);
            Set(storesGroup, look.Stores);
            Set(shellGroup, look.Shell);
            if (shellVector != null && shellVector.canvasRenderer.GetAlpha() != look.Shell) shellVector.canvasRenderer.SetAlpha(look.Shell);
            Set(scopeGroup, look.Page);
            Set(statusGroup, look.Page);
            for (int i = 1; i < pageSurfaces.Length; i++) if (pageSurfaces[i] != null) Set(pageSurfaces[i].Group, look.Wing);
            Set(logGroup, look.Log * look.All);
            Set(cueGroup, look.Cue * look.All);
            Set(routeGroup, look.Cue * look.All);
            Set(cornerGroup, look.Comms * look.All);
            bootDimmed = look.Stores < 1f || look.Shell < 1f || look.Page < 1f || look.Wing < 1f || look.Log < 1f || look.Cue < 1f || look.Comms < 1f || look.All < 1f;
        }

        private static void Set(CanvasGroup group, float alpha)
        {
            if (group != null && group.alpha != alpha) group.alpha = alpha;
        }

        /// <summary>The look is not at rest (a power-on or a reaction left something dimmed; the groups are created while it runs too): the owner asks, to give it back once.</summary>
        public bool BootDimmed => bootDimmed;
    }
}
