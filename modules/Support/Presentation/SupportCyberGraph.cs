using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>One directed dependency: <see cref="Parent"/> gates <see cref="Child"/>.</summary>
    internal readonly struct CyberEdge
    {
        public readonly FacilityId Parent;
        public readonly FacilityId Child;

        public CyberEdge(FacilityId parent, FacilityId child)
        {
            Parent = parent;
            Child = child;
        }
    }

    /// <summary>Where one facility sits in the drawn graph.</summary>
    internal readonly struct CyberNodeLayout
    {
        public readonly FacilityId Id;

        /// <summary>Which independent root this facility's chain traces back to.</summary>
        public readonly int Lane;

        /// <summary>Depth from that root; 0 for a root itself.</summary>
        public readonly int Rank;

        public CyberNodeLayout(FacilityId id, int lane, int rank)
        {
            Id = id;
            Lane = lane;
            Rank = rank;
        }
    }

    /// <summary>
    /// Derives the CYBER tab's node-graph geometry from <see cref="InfoNetwork.Facilities"/>'
    /// real prerequisite data, rather than a hand-drawn shape — a facility only gates on its
    /// <see cref="FacilityInfo.Prerequisite"/> when <see cref="FacilityInfo.PrerequisiteLevel"/>
    /// is above zero (matching <see cref="InfoNetwork.CanUpgrade"/> exactly), so the drawn tree
    /// can never silently show a dependency that isn't actually enforced.
    /// </summary>
    internal static class SupportCyberGraph
    {
        public static readonly CyberNodeLayout[] Nodes = BuildNodes();
        public static readonly CyberEdge[] Edges = BuildEdges();
        public static readonly int LaneCount = CountLanes();

        public static CyberNodeLayout Layout(FacilityId id) => Nodes[(int)id];

        private static CyberNodeLayout[] BuildNodes()
        {
            FacilityInfo[] facilities = InfoNetwork.Facilities;
            var result = new CyberNodeLayout[facilities.Length];
            var laneOf = new int[facilities.Length];
            for (int i = 0; i < laneOf.Length; i++) laneOf[i] = -1;

            int nextLane = 0;
            for (int i = 0; i < facilities.Length; i++)
            {
                if (facilities[i].PrerequisiteLevel > 0) continue;
                laneOf[i] = nextLane++;
                result[i] = new CyberNodeLayout(facilities[i].Id, laneOf[i], 0);
            }

            // The table is small and never deep; a few passes always resolve every child
            // once its parent's lane is known.
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < facilities.Length; i++)
                {
                    if (laneOf[i] >= 0) continue;
                    int parentIndex = (int)facilities[i].Prerequisite;
                    if (laneOf[parentIndex] < 0) continue;
                    laneOf[i] = laneOf[parentIndex];
                    result[i] = new CyberNodeLayout(facilities[i].Id, laneOf[i], result[parentIndex].Rank + 1);
                    changed = true;
                }
            }

            return result;
        }

        private static CyberEdge[] BuildEdges()
        {
            FacilityInfo[] facilities = InfoNetwork.Facilities;
            var edges = new List<CyberEdge>(facilities.Length);
            for (int i = 0; i < facilities.Length; i++)
                if (facilities[i].PrerequisiteLevel > 0)
                    edges.Add(new CyberEdge(facilities[i].Prerequisite, facilities[i].Id));
            return edges.ToArray();
        }

        private static int CountLanes()
        {
            int max = -1;
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i].Lane > max) max = Nodes[i].Lane;
            return max + 1;
        }
    }
}
