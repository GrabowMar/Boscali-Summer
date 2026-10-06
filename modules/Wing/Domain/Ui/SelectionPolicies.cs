using System;

namespace BoscaliSummer.Modules.Wing.Domain
{

    /// <summary>Keep tactical member interaction independent of weapon-target selection.</summary>
    internal static class MapSelectionPolicy
    {
        public static bool IsBehindMap(int sortPriority, int renderPriority,
                                       int mapSortPriority, int mapRenderPriority) =>
            sortPriority < mapSortPriority ||
            (sortPriority == mapSortPriority && renderPriority < mapRenderPriority);

        public static bool DeferToMouseClick(bool controllerSource, bool mouseGestureActive,
                                            bool pointerOverIcon)
        {
            return controllerSource && mouseGestureActive && pointerOverIcon;
        }

        public static bool IconReceivesPointer(bool isPlayerAircraft, bool nativeSelected,
                                               bool isWingMember, bool tacticalCommandsActive)
        {
            if (isPlayerAircraft) return false;
            return !nativeSelected || (isWingMember && tacticalCommandsActive);
        }
    }

    /// <summary>Roster pilot cycling and automatic selection advancement.</summary>
    public static class PilotSelectionPolicy
    {
        /// <summary>Choose the next free index with wraparound; if none are free, advance one index
        /// cyclically.</summary>
        public static int NextIndex(int startIndex, int totalCount, Func<int, bool> isFree)
        {
            if (totalCount <= 0) return -1;
            if (startIndex < 0 || startIndex >= totalCount) startIndex = 0;

            for (int i = 1; i <= totalCount; i++)
            {
                int candidate = (startIndex + i) % totalCount;
                if (isFree != null && isFree(candidate))
                {
                    return candidate;
                }
            }

            return (startIndex + 1) % totalCount;
        }
    }
}
