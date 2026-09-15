using System.Collections.Generic;

namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// Depth-first order for a staff roster: every post is listed directly after the post it
    /// reports to, with that post's whole branch before the next branch begins.
    ///
    /// <para>The host lists slots in generation order, which says nothing about reporting —
    /// a base commander's slot number does not tell you which component commander it answers
    /// to. The console draws one trunk per tier, and a trunk only means what it says if a
    /// post sits directly under the post it answers to, so the roster is re-ordered here.
    /// Display order only: identity, slot and parent stay the host's.</para>
    ///
    /// <para>Bounded by the roster it is given. A post whose parent is not in the list leads
    /// its own branch, and a cycle (which the host does not produce) leaves the input order
    /// alone instead of looping or dropping a post.</para>
    /// </summary>
    internal static class CommandRosterOrder
    {
        /// <summary>
        /// Write the post indices into <paramref name="order"/>, a post and its whole branch
        /// before the next, and return how many were written. Every index appears exactly
        /// once. <paramref name="visited"/> is scratch space the caller owns.
        /// </summary>
        public static int Sort(
            int count, IReadOnlyList<int> ids, IReadOnlyList<int> parents, int[] order, bool[] visited)
        {
            if (ids == null || parents == null || order == null || visited == null) return 0;

            int limit = count;
            if (limit > ids.Count) limit = ids.Count;
            if (limit > parents.Count) limit = parents.Count;
            if (limit > order.Length) limit = order.Length;
            if (limit > visited.Length) limit = visited.Length;
            if (limit <= 0) return 0;

            for (int i = 0; i < limit; i++) visited[i] = false;

            int written = 0;
            for (int i = 0; i < limit; i++)
            {
                if (ReportsInto(ids, parents, limit, i)) continue;
                written = Emit(i, ids, parents, order, visited, limit, written);
            }

            // A post whose parent is only reachable through a cycle keeps the host's order
            // rather than being dropped off the roster.
            for (int i = 0; i < limit && written < limit; i++)
            {
                written = Emit(i, ids, parents, order, visited, limit, written);
            }

            return written;
        }

        /// <summary>True when the post's parent is another post in this roster.</summary>
        private static bool ReportsInto(IReadOnlyList<int> ids, IReadOnlyList<int> parents, int limit, int index)
        {
            int parent = parents[index];
            if (parent == ids[index]) return false;

            for (int j = 0; j < limit; j++)
            {
                if (j != index && ids[j] == parent) return true;
            }
            return false;
        }

        private static int Emit(
            int index, IReadOnlyList<int> ids, IReadOnlyList<int> parents,
            int[] order, bool[] visited, int limit, int written)
        {
            if (visited[index]) return written;
            visited[index] = true;
            order[written++] = index;

            for (int j = 0; j < limit && written < limit; j++)
            {
                if (visited[j] || parents[j] != ids[index]) continue;
                written = Emit(j, ids, parents, order, visited, limit, written);
            }
            return written;
        }
    }
}
