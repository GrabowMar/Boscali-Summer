using System;

namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// How the OPERATIONS page divides the bay it is given between its two lists.
    ///
    /// <para>The page is drawn once and re-laid out, and this is the decision it re-lays out
    /// to: how many rows the bay can hold at all, how those rows split between the target
    /// list and the reinforcement list, and how far the pitch may spread to fill the height.
    /// It is pure because it is the part that cannot be seen in a screenshot — a page that
    /// reserves rows its data never fills is exactly the defect this arithmetic exists to
    /// prevent, and a wrong split hides it.</para>
    /// </summary>
    internal static class OperationsBoardFit
    {
        /// <summary>Rows the target list may show at once; the rest are counted in its note.</summary>
        internal const int TargetMaximum = 4;

        /// <summary>Rows the reinforcement list may show at once.</summary>
        internal const int ReinforceMaximum = 6;

        /// <summary>Fewer rows than this and the page would rather scroll than show a stub.</summary>
        internal const int RowMinimum = 4;

        internal const int TargetMinimum = 2;
        internal const int ReinforceMinimum = 2;

        /// <summary>Whether the bay holds a full page at the base pitch.</summary>
        internal static bool Fits(float space, float pitch) =>
            space > 0f && pitch > 0f && (int)Math.Floor(space / pitch) >= RowMinimum;

        /// <summary>Total list rows the page shows. A bay that does not fit shows its full windows and scrolls.</summary>
        internal static int Rows(float space, float pitch, bool fits)
        {
            int all = TargetMaximum + ReinforceMaximum;
            if (!fits || pitch <= 0f) return all;

            int capacity = (int)Math.Floor(space / pitch);
            return capacity < RowMinimum ? RowMinimum : capacity > all ? all : capacity;
        }

        /// <summary>
        /// The target list takes the odd row: it is the page's primary reading and the picker
        /// while an operation arms.
        /// </summary>
        internal static int Targets(int rows) =>
            Clamp((int)Math.Ceiling(rows * 0.45f), TargetMinimum, TargetMaximum);

        /// <summary>What is left of the split goes to reinforcement, inside its own ceiling.</summary>
        internal static int Reinforce(int rows, int targets) =>
            Clamp(rows - targets, ReinforceMinimum, ReinforceMaximum);

        /// <summary>
        /// The pitch the rows are drawn at: the base pitch, spread to fill a taller bay but
        /// never past the cap that keeps a list looking like a list.
        /// </summary>
        internal static float Pitch(float space, int rows, float basePitch, float maximum)
        {
            if (rows <= 0 || space <= 0f) return basePitch;
            float spread = space / rows;
            if (spread < basePitch) return basePitch;
            return spread > maximum ? maximum : spread;
        }

        private static int Clamp(int value, int minimum, int maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
