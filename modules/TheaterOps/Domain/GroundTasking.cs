using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>What the tasking rule knows about one ground group; X and Z are the centroid of its live members.</summary>
    internal readonly struct GroupFacts
    {
        /// <summary>Group id; the last tie-break.</summary>
        internal readonly int Id;
        /// <summary>Centroid of the live members, metres.</summary>
        internal readonly float X, Z;
        /// <summary>Alive, not withdrawing, and not stalled on this effort.</summary>
        internal readonly bool Usable;
        /// <summary>The group's own OnEffort flag; never "its key equals the effort's key".</summary>
        internal readonly bool Held;
        /// <summary>Depot-born within WaveSeconds, or on the effort because of that: chosen at any distance and outside the quota.</summary>
        internal readonly bool Wave;

        /// <summary>Builds the facts of one group.</summary>
        internal GroupFacts(int id, float x, float z, bool usable, bool held, bool wave)
        { Id = id; X = x; Z = z; Usable = usable; Held = held; Wave = wave; }
    }

    /// <summary>One ground group that still accepts vehicles.</summary>
    internal readonly struct OpenGroup
    {
        /// <summary>Group id; the tie-break.</summary>
        internal readonly int Id;
        /// <summary>Position of the first live member, metres.</summary>
        internal readonly float LeaderX, LeaderZ;
        /// <summary>Live members now.</summary>
        internal readonly int Members;
        /// <summary>Seconds since the group was created.</summary>
        internal readonly float AgeSeconds;

        /// <summary>Builds one open group.</summary>
        internal OpenGroup(int id, float leaderX, float leaderZ, int members, float ageSeconds)
        { Id = id; LeaderX = leaderX; LeaderZ = leaderZ; Members = members; AgeSeconds = ageSeconds; }
    }

    /// <summary>Which of a faction's ground groups carry its main effort, and which keep a front. Pure; deterministic for any input order.</summary>
    internal static class GroundTasking
    {
        /// <summary>A group joins the effort from this far, metres.</summary>
        internal const float ReachMeters = 20000f;
        /// <summary>A group already on the effort is kept to this far, metres.</summary>
        internal const float HeldReachMeters = 25000f;
        /// <summary>A group off the effort holds a sensed front this close, metres.</summary>
        internal const float FrontReachMeters = 12000f;
        /// <summary>A depot-born group is a wave group for this long, seconds.</summary>
        internal const float WaveSeconds = 120f;

        /// <summary>Two thirds of the usable groups in reach, rounded up, at least one; a reconnaissance takes one.</summary>
        internal static int Quota(TheaterOpKind kind, int usable) =>
            usable <= 0 ? 0 : kind == TheaterOpKind.Recon ? 1 : (int)((2L * usable + 2) / 3);

        /// <summary>Marks the groups on the effort in chosen (indexed like groups) and returns how many; order: held, nearest to the fix, lowest id.</summary>
        internal static int Choose(GroupFacts[] groups, int count, float fixX, float fixZ, TheaterOpKind kind, bool[] chosen)
        {
            if (groups == null || chosen == null || count <= 0 || count > groups.Length || count > chosen.Length) return 0;
            int inReach = 0, taken = 0;
            for (int i = 0; i < count; i++)
            {
                chosen[i] = false;
                if (!groups[i].Usable) continue;
                if (groups[i].Wave) { chosen[i] = true; taken++; }
                else if (InReach(groups[i], fixX, fixZ)) inReach++;
            }
            int quota = Quota(kind, inReach);
            for (int picked = 0; picked < quota; picked++)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if (chosen[i] || groups[i].Wave || !InReach(groups[i], fixX, fixZ)) continue;
                    if (best < 0 || Before(groups[i], groups[best], fixX, fixZ)) best = i;
                }
                if (best < 0) break;
                chosen[best] = true;
                taken++;
            }
            return taken;
        }

        /// <summary>Index of the nearest front within FrontReachMeters (lowest index on a tie), else -1.</summary>
        internal static int NearestFront(float x, float z, float[] frontX, float[] frontZ, int count)
        {
            if (frontX == null || frontZ == null || count <= 0 || count > frontX.Length || count > frontZ.Length) return -1;
            int best = -1;
            float bestSquare = FrontReachMeters * FrontReachMeters;
            for (int i = 0; i < count; i++)
            {
                float square = Square(x, z, frontX[i], frontZ[i]);
                if (!(square <= bestSquare)) continue;
                if (best >= 0 && square >= bestSquare) continue;
                best = i;
                bestSquare = square;
            }
            return best;
        }

        private static bool InReach(GroupFacts group, float fixX, float fixZ)
        {
            if (!group.Usable) return false;
            float reach = group.Held ? HeldReachMeters : ReachMeters;
            return Square(group.X, group.Z, fixX, fixZ) <= reach * reach;
        }

        private static bool Before(GroupFacts a, GroupFacts b, float fixX, float fixZ)
        {
            if (a.Held != b.Held) return a.Held;
            float da = Square(a.X, a.Z, fixX, fixZ), db = Square(b.X, b.Z, fixX, fixZ);
            if (da != db) return da < db;
            return a.Id < b.Id;
        }

        private static float Square(float ax, float az, float bx, float bz)
        { float dx = ax - bx, dz = az - bz; return dx * dx + dz * dz; }
    }

    /// <summary>Which group a freshly seen vehicle joins, and which lane it takes.</summary>
    internal static class GroupFormation
    {
        /// <summary>A group younger than this forms around its leader and is sealed at this age, seconds.</summary>
        internal const float JoinSeconds = 20f;
        /// <summary>A young group takes a vehicle this close to its leader, metres.</summary>
        internal const float JoinRadiusMeters = 800f;
        /// <summary>An older group with room still takes a straggler this close, metres.</summary>
        internal const float StragglerRadiusMeters = 3000f;

        /// <summary>Index of the group whose leader is nearest (lowest id on a tie), else -1; callers list only live, non-withdrawing groups of the vehicle's faction.</summary>
        internal static int Pick(OpenGroup[] open, int count, float x, float z)
        {
            if (open == null || count <= 0 || count > open.Length) return -1;
            int best = -1;
            float bestSquare = 0f;
            for (int i = 0; i < count; i++)
            {
                OpenGroup group = open[i];
                if (group.Members >= FrontlineTactics.GroupSize) continue;
                float reach = group.AgeSeconds < JoinSeconds ? JoinRadiusMeters : StragglerRadiusMeters;
                float dx = group.LeaderX - x, dz = group.LeaderZ - z;
                float square = dx * dx + dz * dz;
                if (!(square <= reach * reach)) continue;
                if (best >= 0 && (square > bestSquare || square == bestSquare && group.Id > open[best].Id)) continue;
                best = i;
                bestSquare = square;
            }
            return best;
        }

        /// <summary>Lowest lane (0 to 5) whose bit is clear in usedMask, else -1.</summary>
        internal static int FreeLane(int usedMask)
        {
            for (int lane = 0; lane < FrontlineTactics.GroupSize; lane++)
                if ((usedMask & (1 << lane)) == 0) return lane;
            return -1;
        }
    }
}
